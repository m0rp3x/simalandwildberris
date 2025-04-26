using System.Text.Json;
using Shared.Enums;
using WBSL.Data.HttpClientFactoryExt;

namespace WBSL.Data.Services.Wildberries;

using Microsoft.Extensions.Caching.Memory;

public class BoxTariffService
{
    private readonly PlatformHttpClientFactory _httpFactory;
    private readonly IMemoryCache _cache;
    private const string CacheKey = "WildberriesBoxTariffs";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    public BoxTariffService(PlatformHttpClientFactory httpFactory, IMemoryCache cache)
    {
        _httpFactory = httpFactory;
        _cache = cache;
    }

    public async Task<(decimal basePrice, decimal literPrice)> GetCurrentBoxTariffsAsync(int accountId)
    {
        if (_cache.TryGetValue(CacheKey, out (decimal basePrice, decimal literPrice) cachedTariffs))
        {
            return cachedTariffs;
        }

        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");

        var client = await _httpFactory.CreateClientAsync(ExternalAccountType.WildBerriesCommonApi, accountId, sync: true);
        
        var response = await client.GetAsync($"/api/v1/tariffs/box?date={today}");
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to fetch box tariffs: {error}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var tariffResponse = JsonSerializer.Deserialize<BoxTariffApiResponse>(json);

        var warehouse = tariffResponse?.response?.data?.warehouseList
            ?.FirstOrDefault(w => w.warehouseName == "Маркетплейс");

        if (warehouse == null)
            throw new Exception("Warehouse 'Маркетплейс' not found in tariffs.");

        var basePrice = decimal.Parse(warehouse.boxDeliveryBase);
        var literPrice = decimal.Parse(warehouse.boxDeliveryLiter);

        _cache.Set(CacheKey, (basePrice, literPrice), CacheDuration);

        return (basePrice, literPrice);
    }
}

public class BoxTariffApiResponse
{
    public BoxTariffResponse response { get; set; }
}

public class BoxTariffResponse
{
    public BoxTariffData data { get; set; }
}

public class BoxTariffData
{
    public List<WarehouseTariff> warehouseList { get; set; }
}

public class WarehouseTariff
{
    public string boxDeliveryBase { get; set; }
    public string boxDeliveryLiter { get; set; }
    public string warehouseName { get; set; }
}
