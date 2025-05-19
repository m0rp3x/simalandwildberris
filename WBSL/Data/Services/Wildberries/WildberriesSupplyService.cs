using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Enums;
using WBSL.Data.Enums;
using WBSL.Data.Errors;
using WBSL.Data.Extensions;
using WBSL.Data.HttpClientFactoryExt;
using WBSL.Data.Models;
using WBSL.Models;

namespace WBSL.Data.Services.Wildberries;

public class WildberriesSupplyService
{
    private readonly QPlannerDbContext _db;
    private readonly PlatformHttpClientFactory _httpFactory;
    private readonly ILogger<WildberriesSupplyService> _logger;

    public WildberriesSupplyService(
        QPlannerDbContext db,
        PlatformHttpClientFactory httpFactory,
        ILogger<WildberriesSupplyService> logger){
        _db = db;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <summary>
    /// Создаёт новую поставку в Wildberries и привязывает к ней указанный заказ.
    /// При ошибке возвращает SupplyResult со всеми деталями (SupplyId, ErrorCode, Message).
    /// </summary>
    /// <param name="date">Дата для имени поставки.</param>
    /// <param name="orderId">ID заказа, который нужно привязать.</param>
    /// <param name="ct">Канал отмены.</param>
    public async Task<SupplyResult> CreateSupplyAndAttachOrderAsync(
        DateTime date,
        long orderId,
        CancellationToken ct = default){
        // 1) Получить и проверить заказ
        var order = await GetOrderAsync(orderId, ct);
        if (order.Status != OrderStatus.New)
            throw new InvalidOperationException($"Order {orderId} имеет статус {order.Status}, ожидался New.");

        // 2) Найти валидный клиент по складу заказа
        var client = await GetValidClientForWarehouseAsync(order.WarehouseId, ct);

        // 3) Создать поставку в WB
        var supplyId = await CreateSupplyAsync(client, date, ct);

        // 4) Добавить сборочное задание (сам order) к поставке
        var addResult = await TryAddOrderToSupplyAsync(client, supplyId, orderId, ct);
        if (!addResult.Success){
            return new SupplyResult{
                Success = false,
                SupplyId = supplyId,
                ErrorCode = addResult.ErrorCode,
                Message = addResult.Message
            };
        }

        bool dbOk = false;
        string? dbError = null;
        try{
            order.Status = OrderStatus.Confirm;
            _db.Orders.Update(order);
            await _db.SaveChangesAsync(ct);
            dbOk = true;
        }
        catch (Exception ex){
            _logger.LogError(ex, "Ошибка при обновлении статуса заказа {OrderId} в БД", orderId);
            dbError = ex.Message;
        }

        _logger.LogInformation("Order {OrderId} успешно привязан к поставке {SupplyId}", orderId, supplyId);
        return new SupplyResult{
            Success = true, // WB OK
            SupplyId = supplyId,
            DbUpdated = dbOk, // true если БД сохранилась
            ErrorCode = dbOk ? null : "DB_ERROR",
            Message = dbOk ? null : dbError
        };
    }

    /// <summary>
    /// Извлекает OrderEntity из БД, либо кидает, если не найден.
    /// </summary>
    private async Task<OrderEntity> GetOrderAsync(long orderId, CancellationToken ct){
        var order = await _db.Orders.FindAsync(new object[]{ orderId }, ct);
        return order ?? throw new InvalidOperationException($"Order {orderId} не найден в базе.");
    }

    /// <summary>
    /// Находит первый валидный HttpClient для заданного warehouseId, пингуя аккаунты.
    /// </summary>
    private async Task<HttpClient> GetValidClientForWarehouseAsync(int warehouseId, CancellationToken ct){
        // Извлекаем все accountId для склада
        var accountIds = await _db.Set<ExternalAccountWarehouse>()
            .Where(x => x.ExternalAccount.platform == ExternalAccountType.Wildberries.ToString()
                        && x.WarehouseId == warehouseId)
            .Select(x => x.ExternalAccountId)
            .ToListAsync(ct);

        if (!accountIds.Any()){
            accountIds = await _db.external_accounts
                .Where(x => x.platform == ExternalAccountType.Wildberries.ToString()
                            && x.warehouseid == warehouseId)
                .Select(x => x.id)
                .ToListAsync(ct);
        }

        if (!accountIds.Any())
            throw new InvalidOperationException($"Нет external_accounts для warehouse {warehouseId}");

        var client = await _httpFactory.GetValidClientAsync(
            ExternalAccountType.WildBerriesMarketPlace,
            accountIds,
            "/ping",
            ct);

        return client ??
               throw new InvalidOperationException($"Не удалось найти валидный WB-клиент для warehouse {warehouseId}");
    }

    /// <summary>
    /// Создаёт новую поставку через POST /api/v3/supplies, возвращает supplyId.
    /// </summary>
    private async Task<string> CreateSupplyAsync(HttpClient client, DateTime date, CancellationToken ct){
        var supplyName = date.ToString("yyyy-MM-dd HH:mm:ss");

        var payload = new{
            name = supplyName
        };

        var resp = await client.PostAsJsonAsync("/api/v3/supplies", payload, ct);
        if (!resp.IsSuccessStatusCode){
            var body = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogError("CreateSupplyAsync failed ({Status}): {Body}",
                resp.StatusCode, body);
            throw new InvalidOperationException(
                $"CreateSupplyAsync returned {(int)resp.StatusCode}");
        }

        // Ответ JSON: { "id": "WB-GI-1234567" }
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("id", out var idProp))
            throw new InvalidOperationException("Response has no 'id' field");

        var supplyId = idProp.GetString();
        if (string.IsNullOrEmpty(supplyId))
            throw new InvalidOperationException("'id' field is empty");

        return supplyId;
    }

    /// <summary>
    /// Привязывает order к существующей поставке через PATCH.
    /// </summary>
    private async Task<SupplyResult> TryAddOrderToSupplyAsync(
        HttpClient client,
        string supplyId,
        long orderId,
        CancellationToken ct){
        var url = $"/api/v3/supplies/{supplyId}/orders/{orderId}";
        var payload = new{ orderId };
        var req = new HttpRequestMessage(HttpMethod.Patch, url){
            Content = JsonContent.Create(payload)
        };

        var resp = await client.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.Conflict){
            var err = await resp.Content.ReadFromJsonAsync<WildberriesErrorResponse>(cancellationToken: ct);
            return new SupplyResult{
                Success = false,
                ErrorCode = err?.Code,
                Message = err?.Message,
                SupplyId = supplyId
            };
        }

        if (!resp.IsSuccessStatusCode){
            return new SupplyResult{
                Success = false,
                ErrorCode = resp.StatusCode.ToString(),
                Message = $"HTTP {(int)resp.StatusCode}",
                SupplyId = supplyId
            };
        }

        return new SupplyResult{ Success = true, SupplyId = supplyId };
    }
}