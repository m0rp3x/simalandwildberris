using System.Text;
using System.Text.Json;
using Shared.Enums;
using WBSL.Data.Errors;
using WBSL.Data.HttpClientFactoryExt;

namespace WBSL.Data.Services.Wildberries;

public class WildberriesPriceService : WildberriesBaseService
{
    private readonly QPlannerDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PriceCalculatorService _priceCalculator;
    private readonly int maxRetries = 3;
    protected override Task<HttpClient> GetWbClientAsync(int accountId, bool isSync = false) 
        => _clientFactory.CreateClientAsync(ExternalAccountType.WildBerriesDiscountPrices, accountId, isSync);
    
    public WildberriesPriceService(
        PlatformHttpClientFactory httpFactory,
        QPlannerDbContext db, IServiceScopeFactory scopeFactory, PriceCalculatorService priceCalculator) : base(httpFactory){
        _db = db;
        _scopeFactory = scopeFactory;
        _priceCalculator = priceCalculator;
    }
    
    public async Task<bool> PushPricesToWildberriesAsync(List<long> nmIds, int accountId, bool isSync = false)
    {
        if (nmIds == null || nmIds.Count == 0)
        {
            throw new ArgumentException("No nmIds provided for price upload.", nameof(nmIds));
        }

        var client = await GetWbClientAsync(accountId, isSync);
        
        var payloadData = new List<object>();

        foreach (var nmId in nmIds)
        {
            var price = await _priceCalculator.CalculatePriceAsync(nmId, accountId);
            var discount = 0;

            payloadData.Add(new
            {
                nmId = nmId,
                price = price,
                discount = discount
            });
        }

        var payload = new
        {
            data = payloadData
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v2/upload/task", content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new WildberriesApiException(
                $"Wildberries API returned error {response.StatusCode}",
                (int)response.StatusCode,
                errorContent
            );
        }
        else{
            return true;
        }
    }
}