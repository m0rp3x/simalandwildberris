using System.Text.Json;
using Shared.Enums;
using WBSL.Data.HttpClientFactoryExt;

namespace WBSL.Data.Services.Wildberries;

using Microsoft.Extensions.Caching.Memory;

public class CommissionService
{
    private readonly PlatformHttpClientFactory _httpFactory;
    private readonly IMemoryCache _cache;

    private const string CacheKeyPrefix = "WildberriesCommissions";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6); // Обновлять раз в 6 часов

    public CommissionService(PlatformHttpClientFactory httpFactory, IMemoryCache cache)
    {
        _httpFactory = httpFactory;
        _cache = cache;
    }

    public async Task<decimal?> GetCommissionPercentAsync(int subjectId, int accountId){
        var commissions = await GetCommissionsAsync(accountId);

        var commission = commissions.FirstOrDefault(x => x.subjectID == subjectId);
        return commission?.kgvpMarketplace;
    }

    private async Task<List<CommissionReport>> GetCommissionsAsync(int accountId){
        var cacheKey = $"{CacheKeyPrefix}{accountId}";

        if (_cache.TryGetValue(cacheKey, out List<CommissionReport> cachedReports))
        {
            return cachedReports;
        }
        
        var client = await _httpFactory.CreateClientAsync(ExternalAccountType.WildBerriesCommonApi, accountId, sync: true);

        var response = await client.GetAsync("/api/v1/tariffs/commission");
        if (!response.IsSuccessStatusCode){
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to fetch Wildberries commissions: {error}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var commissionResponse = JsonSerializer.Deserialize<CommissionApiResponse>(json);

        var reports = commissionResponse?.report ?? new List<CommissionReport>();

        _cache.Set(cacheKey, reports, CacheDuration);

        return reports;
    }
}

public class CommissionApiResponse
{
    public List<CommissionReport> report{ get; set; } = new();
}

public class CommissionReport
{
    public decimal kgvpMarketplace{ get; set; }
    public decimal kgvpSupplier{ get; set; }
    public decimal kgvpSupplierExpress{ get; set; }
    public decimal paidStorageKgvp{ get; set; }
    public int parentID{ get; set; }
    public string parentName{ get; set; }
    public int subjectID{ get; set; }
    public string subjectName{ get; set; }
}