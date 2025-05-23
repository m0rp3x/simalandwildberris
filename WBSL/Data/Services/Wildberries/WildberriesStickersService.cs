using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Enums;
using WBSL.Data.Enums;
using WBSL.Data.Extensions;
using WBSL.Data.HttpClientFactoryExt;

namespace WBSL.Data.Services.Wildberries;

public class WildberriesStickersService
{
    private readonly QPlannerDbContext _db;
    private readonly PlatformHttpClientFactory _httpFactory;
    private readonly ILogger<WildberriesStickersService> _logger;

    public WildberriesStickersService(
        QPlannerDbContext db,
        PlatformHttpClientFactory httpFactory,
        ILogger<WildberriesStickersService> logger){
        _db          = db;
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    /// <summary>
    /// Запрашивает у Wildberries стикеры для заданных заказов и возвращает байты PNG.
    /// </summary>
    /// <param name="orderIds">Список ID заказов.</param>
    /// <param name="type">Тип возвращаемого формата (png/svg).</param>
    /// <param name="width">Ширина стикера в пикселях.</param>
    /// <param name="height">Высота стикера в пикселях.</param>
    public async Task<List<OrderStickerDto>> GetStickersAsync(
        IEnumerable<long> orderIds,
        string type = "png",
        int width = 58,
        int height = 40,
        CancellationToken ct = default){
        // 1) Загружаем заказы и проверяем статус
        var orders = await _db.Orders
                              .Where(o => orderIds.Contains(o.Id))
                              .Select(o => new{ o.Id, o.WarehouseId, o.Status })
                              .ToListAsync(ct);

        // Убедимся, что все заказы найдены и имеют статус >= Confirm
        var missing = orderIds.Except(orders.Select(o => o.Id)).ToList();
        if (missing.Any())
            throw new InvalidOperationException($"Заказы не найдены: {string.Join(",", missing)}");

        var invalid = orders
                      .Where(o => o.Status != OrderStatus.Confirm)
                      .Select(o => o.Id)
                      .ToList();
        if (invalid.Any())
            throw new InvalidOperationException(
                $"Нельзя запросить стикер, статус не Confirm: {string.Join(",", invalid)}");
        
        // 2) Группируем по складам
        var byWarehouse = orders
                          .GroupBy(o => o.WarehouseId)
                          .ToDictionary(g => g.Key, g => g.Select(o => o.Id).ToList());
        
        var accounts = await _db.external_accounts
                               .AsNoTracking()
                               .Where(x => x.platform == ExternalAccountType.Wildberries.ToString()
                                           && x.warehouseid.HasValue)
                               .Select(x => new { x.id, WarehouseId = x.warehouseid!.Value })
                               .ToListAsync(ct);
        
        var accountsByWarehouse = accounts
                                  .GroupBy(a => a.WarehouseId)
                                  .ToDictionary(
                                      g => g.Key,
                                      g => g.Select(a => a.id).ToList()
                                  );
        var result = new List<OrderStickerDto>();

        // 3) Для каждого склада — один вызов
        foreach (var (warehouseId, ids) in byWarehouse){
            var accountsIds = await _db.external_accounts
                                   .AsNoTracking()
                                   .Where(x => x.platform == ExternalAccountType.Wildberries.ToString()
                                               && x.warehouseid.HasValue && x.warehouseid == warehouseId)
                                   .Select(x => x.id)
                                   .ToListAsync(ct);
            
            if (!accountsByWarehouse.TryGetValue(warehouseId, out var accountIds)
                || accountIds.Count == 0)
            {
                _logger.LogWarning("Нет external-аккаунтов для склада {WarehouseId}", warehouseId);
                continue;
            }
            
            var client = await _httpFactory.GetValidClientAsync(
                ExternalAccountType.WildBerriesMarketPlace,
                accountsIds,
                "/ping",
                ct);


            if (client == null){
                _logger.LogWarning("Нет валидного клиента для склад {WarehouseId}", warehouseId);
                continue;
            }

            const int MaxPerRequest = 70;

            for (int offset = 0; offset < ids.Count; offset += MaxPerRequest){
                var batch   = ids.Skip(offset).Take(MaxPerRequest).ToList();
                var url     = $"/api/v3/orders/stickers?type={type}&width={width}&height={height}";
                var payload = new{ orders = batch };

                var resp = await client.PostAsJsonAsync(url, payload, ct);
                if (!resp.IsSuccessStatusCode){
                    _logger.LogError(
                        "Ошибка {Status} при запросе стикеров склада {WarehouseId}, batch starting at {Offset}",
                        resp.StatusCode, warehouseId, offset);
                    continue;
                }

                var wrapper = await resp.Content
                                        .ReadFromJsonAsync<StickerResponse>(cancellationToken: ct);
                if (wrapper?.Stickers == null)
                    continue;

                foreach (var s in wrapper.Stickers){
                    try{
                        result.Add(new OrderStickerDto {
                            OrderId    = s.OrderId,
                            Base64File = s.File,
                            FileType   = type,
                            Width      = width,
                            Height     = height
                        });
                    }
                    catch (Exception ex){
                        _logger.LogWarning(ex, "Не удалось декодировать стикер для заказа {OrderId}", s.OrderId);
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
            }
        }

        return result;
    }

    private class StickerResponse
    {
        public List<StickerDto>? Stickers{ get; set; }
    }

    private class StickerDto
    {
        public string PartA{ get; set; } = null!;
        public string PartB{ get; set; } = null!;
        public string Barcode{ get; set; } = null!;
        public string File{ get; set; } = null!; // base64
        public long OrderId{ get; set; }
    }
}