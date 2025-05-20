using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Shared.Enums;
using WBSL.Data.Enums;
using WBSL.Data.Extensions;
using WBSL.Data.HttpClientFactoryExt;
using WBSL.Data.Models;
using WBSL.Data.Models.DTO;
using WBSL.Models;

namespace WBSL.Data.Services.Wildberries;

public class WildberriesOrdersProcessingService : WildberriesBaseService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PlatformHttpClientFactory _httpFactory;
    private readonly ILogger<WildberriesOrdersProcessingService> _logger;
    private static readonly SemaphoreSlim _runGuard = new(1, 1);

    public WildberriesOrdersProcessingService(
        IServiceScopeFactory scopeFactory,
        PlatformHttpClientFactory httpFactory,
        ILogger<WildberriesOrdersProcessingService> logger) : base(httpFactory){
        _scopeFactory = scopeFactory;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <summary>
    /// Основная точка входа для Hangfire.
    /// Гарантирует, что повторный запуск не будет происходить в течение 1 часа.
    /// </summary>
    public async Task FetchAndSaveOrdersAsync(){
        if (!await _runGuard.WaitAsync(TimeSpan.Zero)){
            _logger.LogWarning("FetchAndSaveOrdersAsync уже запущен, пропускаем этот вызов.");
            return;
        }

        try{
            List<OrderDto> orders;
            try{
                orders = await FetchOrdersByAsync();
            }
            catch (Exception ex){
                _logger.LogError(ex, "Ошибка при получении заказов из Wildberries");
                throw;
            }

            try{
                await SaveOrdersAsync(orders);
            }
            catch (Exception ex){
                _logger.LogError(ex, "Ошибка при сохранении заказов в базу");
                throw;
            }
        }
        finally{
            _runGuard.Release();
        }
    }

    /// <summary>
    /// Возвращает заказы из Wildberries, сгруппированные по warehouseId.
    /// </summary>
    private async Task<List<OrderDto>> FetchOrdersByAsync(){
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QPlannerDbContext>();

        // Получаем список Wildberries-аккаунтов
        var accounts = await db.external_accounts
            .Where(x => x.platform == ExternalAccountType.Wildberries.ToString()
                        && x.warehouseid.HasValue)
            .Select(x => new{ x.id, WarehouseId = x.warehouseid!.Value })
            .ToListAsync();
        
        // 2) Группируем accountId по warehouseId
        var byWarehouse = accounts
            .GroupBy(a => a.WarehouseId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.id).ToList());
        
        // var accountWarehousePairs = await db.Set<ExternalAccountWarehouse>()
        //     .Where(x => x.ExternalAccount.platform == ExternalAccountType.Wildberries.ToString())
        //     .Select(x => new { x.ExternalAccountId, x.WarehouseId })
        //     .ToListAsync();
        //
        // // 2) Группируем accountId по warehouseId
        // var byWarehouse = accountWarehousePairs
        //     .GroupBy(x => x.WarehouseId)
        //     .ToDictionary(
        //         g => g.Key,
        //         g => g.Select(x => x.ExternalAccountId).Distinct().ToList()
        //     );

        var allOrders = new List<OrderDto>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        foreach (var (warehouseId, accountIds) in byWarehouse){
            // найдём первый «живой» клиент
            var client = await _httpFactory.GetValidClientAsync(
                ExternalAccountType.WildBerriesMarketPlace,
                accountIds,
                "/ping",
                cts.Token);

            if (client == null){
                _logger.LogWarning(
                    "Нет валидного Wildberries-клиента для склада {WarehouseId}",
                    warehouseId);
                continue;
            }

            HttpResponseMessage response;
            try{
                response = await client.GetAsync("/api/v3/orders/new", cts.Token);
            }
            catch (Exception ex){
                _logger.LogWarning(ex,
                    "Ошибка при запросе заказов для склада {WarehouseId}, аккаунты {AccountIds}",
                    warehouseId, string.Join(",", accountIds));
                continue;
            }

            if (!response.IsSuccessStatusCode){
                _logger.LogWarning(
                    "Неожиданный статус {StatusCode} при запросе заказов для склада {WarehouseId}",
                    (int)response.StatusCode, warehouseId);
                continue;
            }

            OrdersResponseDto? wrapper;
            try{
                await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                wrapper = await JsonSerializer.DeserializeAsync<OrdersResponseDto>(
                    stream,
                    new JsonSerializerOptions{ PropertyNameCaseInsensitive = true },
                    cts.Token);
            }
            catch (Exception ex){
                _logger.LogWarning(ex,
                    "Не удалось десериализовать ответ для склада {WarehouseId}", warehouseId);
                continue;
            }

            if (wrapper?.Orders is{ Count: > 0 } orders)
                allOrders.AddRange(orders);
        }
        var distinct = allOrders
            .DistinctBy(o => o.Id)
            .ToList();
        return distinct;
    }

    /// <summary>
    /// Сохраняет список заказов в базу, преобразуя DTO в сущности.
    /// </summary>
    private async Task SaveOrdersAsync(IEnumerable<OrderDto> orders){
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QPlannerDbContext>();

        foreach (var dto in orders){
            // Здесь вы можете делать Upsert: либо Add, либо Update, если уже есть
            var existing = await db.Orders.FindAsync(dto.Id);
            if (existing != null){
                // Обновляем поля
                existing.Price = dto.Price;
                existing.ConvertedPrice = dto.ConvertedPrice;
                existing.CurrencyCode = dto.CurrencyCode;
                existing.ConvertedCurrencyCode = dto.ConvertedCurrencyCode;
                existing.IsZeroOrder = dto.IsZeroOrder;
                // ... и другие поля по необходимости
                db.Orders.Update(existing);
            }
            else{
                var entity = new OrderEntity{
                    Id = dto.Id,
                    Address = dto.Address?.ToString(),
                    Status = OrderStatus.New,
                    SupplyId = null,
                    UserId = dto.UserId,
                    SalePrice = dto.SalePrice,
                    DeliveryType = dto.DeliveryType,
                    Comment = dto.Comment,
                    OrderUid = dto.OrderUid,
                    Article = dto.Article,
                    CreatedAt = dto.CreatedAt,
                    WarehouseId = dto.WarehouseId,
                    Rid = dto.Rid,
                    NmId = dto.NmId,
                    ChrtId = dto.ChrtId,
                    Price = dto.Price,
                    ConvertedPrice = dto.ConvertedPrice,
                    CurrencyCode = dto.CurrencyCode,
                    ConvertedCurrencyCode = dto.ConvertedCurrencyCode,
                    CargoType = dto.CargoType,
                    IsZeroOrder = dto.IsZeroOrder,
                    Offices = dto.Offices,
                    Skus = dto.Skus,
                    IsB2B = dto.Options.IsB2B
                };
                db.Orders.Add(entity);
            }
        }

        await db.SaveChangesAsync();
    }
}