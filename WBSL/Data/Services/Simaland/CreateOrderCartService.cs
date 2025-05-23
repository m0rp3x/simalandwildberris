using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Shared;
using WBSL.Data;
using WBSL.Data.Models;

public interface ICreateOrderCart
{
    Task SyncOrdersAsync(); 
}

public class CreateOrderCartService : ICreateOrderCart
{
    private readonly IHttpClientFactory   _httpFactory;
    private readonly QPlannerDbContext    _db;
    private readonly IConfiguration       _cfg;
    private readonly ILogger<CreateOrderCartService> _log;
    private readonly int                  _paymentTypeId;
    private readonly int                  _deliveryTypeId;

    public CreateOrderCartService(
        IHttpClientFactory httpFactory,
        QPlannerDbContext  db,
        IConfiguration     cfg,
        ILogger<CreateOrderCartService> log)
    {
        _httpFactory     = httpFactory;
        _db              = db;
        _cfg             = cfg;
        _log             = log;
        _paymentTypeId  = _cfg.GetValue<int>("SimaLand:PaymentTypeId");
        _deliveryTypeId = _cfg.GetValue<int>("SimaLand:DeliveryTypeId");

    }

    // Получение текущих позиций в корзине
    private async Task<Dictionary<long,int>> GetCartAsync(CancellationToken ct = default)
    {
        var client = _httpFactory.CreateClient("SimaLand");
        var resp = await client.GetAsync("cart/", ct);
        resp.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var items = doc.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Select(x => new {
                Sid = x.GetProperty("item_sid").GetInt64(),
                Qty = x.GetProperty("qty").GetInt32()
            });
        return items.ToDictionary(x => x.Sid, x => x.Qty);
    }

    // Добавление позиций в корзину
    private async Task AddToCartAsync(long sid, int qty, CancellationToken ct = default)
    {
        var client = _httpFactory.CreateClient("SimaLand");
        var body = new { item_sid = sid, qty };
        var resp = await client.PostAsJsonAsync("cart-item/", body, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            _log.LogError("AddToCart failed for {Sid}: {Status} {Body}", sid, resp.StatusCode, err);
            throw new Exception("SimaLand AddToCart error");
        }
        _log.LogInformation("Added to cart {Sid} x{Qty}", sid, qty);
    }

    // Основной метод синхронизации WB-заказов
    public async Task SyncOrdersAsync()
    {
        using var tx = await _db.Database.BeginTransactionAsync();

        // 1. Берём заказы старше 3х часов, не обработанные
        var threshold = DateTime.UtcNow.AddHours(-3);
        var orders = await _db.Orders
            .Where(o => o.ProcessedAt == null && o.CreatedAt <= threshold)
            .ToListAsync();
        if (!orders.Any()) { _log.LogInformation("SyncOrders: no new orders"); return; }

        // 2. Группируем по Article → кол-во позиций
        var groups = orders
            .GroupBy(o => o.NmId)
            .Select(g => new { Sid = g.First().Article, Qty = g.Count() }) // Article = item_sid
            .ToList();



        // 3. Получаем текущее состояние корзины в SimaLand
        var cart = await GetCartAsync();

        // 4. Добавляем недостающие позиции
        foreach (var g in groups)
        {
            cart.TryGetValue(Convert.ToInt64(g.Sid), out var existing);
            var toAdd = g.Qty - existing;
            if (toAdd > 0)
                await AddToCartAsync(Convert.ToInt64(g.Sid), toAdd);
        }

        // 5. Формируем заявку на заказ и отправляем
        // Преобразуем группы в динамический список для CreateOrderAsync
        var itemsForOrder = groups
            .Select(g => new WBSL.Data.Models.OrderEntity
            {
                Article = g.Sid.ToString(),
                Rid = g.Qty.ToString()
            })
            .ToList();

        try
        {
            var createResult = await CreateOrderAsync(itemsForOrder);
            _log.LogInformation("CreateOrder: заказ создан, id={OrderId}", createResult.OrderId);

            // 6. Помечаем обработанные заказы и сохраняем
            foreach (var o in orders) o.ProcessedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            _log.LogInformation(
                "SyncOrders: processed {Count} orders, created cart order #{OrderId}.",
                orders.Count, createResult.OrderId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ошибка при создании заявки в SimaLand");
            // транзакция не коммитится — корзина сохраняет текущее состояние
        }
    }

    /// <summary>
    /// Собирает и отправляет POST /order/ — новую заявку.
    /// </summary>
    private async Task<CreateOrderResponse> CreateOrderAsync(
        List<OrderEntity> orders,
        CancellationToken ct = default)


    {
        var groups = orders
            .GroupBy(o => o.Article)
            .Select(g => new { Sid = long.Parse(g.Key), Qty = g.Count() })
            .ToList();


        
        var client = _httpFactory.CreateClient("SimaLand");

        // --- 1) Получаем settlement_id по городу ---
        // Предполагаем, что у OrderEntity есть поле DeliveryCity
        string cityName = orders.First().Address?.Split(',').First() ?? "";
        int settlementId = await GetSettlementIdAsync(client, cityName, ct);

        // --- 2) Получаем список складов и берём первый warehouse_id ---
        var warehouses = await client.GetFromJsonAsync<List<WarehouseDto>>("warehouses", ct);
        int warehouseId = warehouses?.FirstOrDefault()?.Id
                          ?? throw new InvalidOperationException("Нет доступных складов");

        // --- 3) Формируем тело заявки ---
        var payload = new
        {
            settlement_id    = settlementId,
            warehouse_id     = warehouseId,
            payment_type_id  = _paymentTypeId,
            delivery_type_id = _deliveryTypeId,

            // список позиций
            items = groups.Select(g => new {
                item_sid = g.Sid,
                qty      = g.Qty
            }).ToArray()
        };

        // --- 4) Отправляем POST /order/ ---
        var resp = await client.PostAsJsonAsync("order/", payload, ct);
        resp.EnsureSuccessStatusCode();

        // --- 5) Десериализуем ID новой заявки ---
        var create = await resp.Content.ReadFromJsonAsync<CreateOrderResponse>(cancellationToken: ct);
        if (create == null || create.OrderId <= 0)
            throw new InvalidOperationException("Непредвиденный ответ при создании заказа");

        return create;
    }

    /// <summary>
    /// GET /settlement/?name={city} → settlement_id
    /// </summary>
    private async Task<int> GetSettlementIdAsync(
        HttpClient client,
        string      cityName,
        CancellationToken ct)
    {
        var url = $"settlement/?name={Uri.EscapeDataString(cityName)}";
        var resp = await client.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();

        // ожидаем: [{ "id": 123, "name":"Москва", … }]
        var arr = await resp.Content.ReadFromJsonAsync<List<SettlementDto>>(cancellationToken: ct);
        return arr?.FirstOrDefault()?.Id
               ?? throw new InvalidOperationException($"Город '{cityName}' не найден");
    }


    // DTO-ответы API
    private class WarehouseDto
    {
        public int    Id   { get; set; }
        public string Name { get; set; } = null!;
    }

    private class SettlementDto
    {
        public int    Id   { get; set; }
        public string Name { get; set; } = null!;
    }

    public class CreateOrderResponse
    {
        [JsonPropertyName("order_id")]
        public long OrderId { get; set; }
    }
}
