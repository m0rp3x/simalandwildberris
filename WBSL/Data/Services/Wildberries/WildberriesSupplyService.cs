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
        _db          = db;
        _httpFactory = httpFactory;
        _logger      = logger;
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
        List<long> orderIds,
        CancellationToken ct = default){
        // 1) Получить и проверить заказ
        var orders = await GetOrdersAsync(orderIds, ct);

        var validOrders   = orders.Where(o => o.Status == OrderStatus.New).ToList();
        var skippedOrders = orders.Where(o => o.Status != OrderStatus.New).ToList();

        if (!validOrders.Any()){
            return new SupplyResult{
                Success   = false,
                SupplyId  = null,
                ErrorCode = "NO_VALID_ORDERS",
                Message =
                    $"Нет заказов в статусе New, будут пропущены: {string.Join(", ", skippedOrders.Select(o => $"{o.Id}({o.Status})"))}"
            };
        }

        var client = await GetValidClientForWarehouseAsync(validOrders[0].WarehouseId, ct);

        // 3) Создать поставку в WB
        var supplyId = await CreateSupplyAsync(client, date, ct);

        // 4) Добавить сборочное задание (сам order) к поставке
        var attachResults = new List<(long OrderId, bool Success, string? ErrorCode, string? Message)>();
        
        foreach (var skip in skippedOrders)
        {
            attachResults.Add((skip.Id, false, "INVALID_STATUS", $"Пропущен, статус={skip.Status}"));
        }
        
        // 5) Цикл: добавляем каждый заказ в поставку
        foreach (var order in validOrders){
            var addResult = await TryAddOrderToSupplyAsync(client, supplyId, order.Id, ct);
            if (!addResult.Success){
                attachResults.Add((order.Id, false, addResult.ErrorCode, addResult.Message));
                continue;
            }

            order.SupplyId = supplyId;
            order.Status   = OrderStatus.Confirm;
            attachResults.Add((order.Id, true, null, null));
        }

        try{
            _db.Orders.UpdateRange(orders);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex){
            _logger.LogError(ex, "Ошибка при массовом обновлении заказов в БД для supply {SupplyId}", supplyId);
            return new SupplyResult{
                Success   = false,
                SupplyId  = supplyId,
                DbUpdated = false,
                ErrorCode = "DB_BATCH_ERROR",
                Message   = ex.Message
            };
        }

        // 8) Формируем общий SupplyResult: успешен, если все added = true
        var allOk = attachResults.All(r => r.Success);
        if (!allOk){
            // можно вернуть подробный лог ошибок в Message
            var errors = attachResults
                         .Where(r => !r.Success)
                         .Select(r => $"{r.OrderId}:{r.ErrorCode}")
                         .ToList();
            return new SupplyResult{
                Success   = false,
                SupplyId  = supplyId,
                DbUpdated = true,
                ErrorCode = "PARTIAL_FAIL",
                Message   = $"Не удалось добавить заказы: {string.Join(", ", errors)}"
            };
        }

        _logger.LogInformation(
            "Заказы [{OrderIds}] успешно привязаны к поставке {SupplyId}",
            string.Join(", ", orderIds), supplyId);

        return new SupplyResult{
            Success   = true, // WB OK
            SupplyId  = supplyId,
            DbUpdated = true
        };
    }

    /// <summary>
    /// Извлекает OrderEntity из БД, либо кидает, если не найден.
    /// </summary>
    private async Task<List<OrderEntity>> GetOrdersAsync(
        List<long> orderIds,
        CancellationToken ct){
        var orders = await _db.Orders
                              .Where(x => orderIds.Contains(x.Id))
                              .ToListAsync(ct);

        // Проверяем, что ни один из запрошенных заказов не пропал
        var foundIds = orders.Select(o => o.Id).ToHashSet();
        var missing  = orderIds.Where(id => !foundIds.Contains(id)).ToList();
        if (missing.Any()){
            throw new InvalidOperationException(
                $"Заказы {string.Join(", ", missing)} не найдены в базе.");
        }

        return orders;
    }

    /// <summary>
    /// Находит первый валидный HttpClient для заданного warehouseId, пингуя аккаунты.
    /// </summary>
    private async Task<HttpClient> GetValidClientForWarehouseAsync(int warehouseId, CancellationToken ct){
        // Извлекаем все accountId для склада
        var accountIds = await _db.external_accounts
                                  .AsNoTracking()
                                  .Where(x => x.platform == ExternalAccountType.Wildberries.ToString()
                                              && x.warehouseid == warehouseId)
                                  .Select(x => x.id)
                                  .ToListAsync(ct);
        if (!accountIds.Any())
            throw new InvalidOperationException(
                $"Для склада {warehouseId} не найдено ни одного external_account");

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
        var       json = await resp.Content.ReadAsStringAsync(ct);
        using var doc  = JsonDocument.Parse(json);
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
        var url     = $"/api/v3/supplies/{supplyId}/orders/{orderId}";
        var payload = new{ orderId };
        var req = new HttpRequestMessage(HttpMethod.Patch, url){
            Content = JsonContent.Create(payload)
        };

        var resp = await client.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.Conflict){
            var err = await resp.Content.ReadFromJsonAsync<WildberriesErrorResponse>(cancellationToken: ct);
            return new SupplyResult{
                Success   = false,
                ErrorCode = err?.Code,
                Message   = err?.Message,
                SupplyId  = supplyId
            };
        }

        if (!resp.IsSuccessStatusCode){
            return new SupplyResult{
                Success   = false,
                ErrorCode = resp.StatusCode.ToString(),
                Message   = $"HTTP {(int)resp.StatusCode}",
                SupplyId  = supplyId
            };
        }

        return new SupplyResult{ Success = true, SupplyId = supplyId };
    }
}