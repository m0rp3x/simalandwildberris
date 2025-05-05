using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Shared;
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
        QPlannerDbContext db, IServiceScopeFactory scopeFactory,
        PriceCalculatorService priceCalculator) : base(httpFactory){
        _db = db;
        _scopeFactory = scopeFactory;
        _priceCalculator = priceCalculator;
    }

    public async Task<PricePushResult> PushPricesToWildberriesAsync(PriceCalculatorSettingsDto settingsDto,
        int accountId, bool isSync = false){
        var result = new PricePushResult();
        
        var client = await GetWbClientAsync(accountId, isSync);
        var nmIds = await _priceCalculator.PrepareCalculationDataAsync(accountId);

        var payloadData = new List<object>();
        foreach (var nmId in nmIds){
            decimal price;
            try
            {
                price = await _priceCalculator
                    .CalculatePriceAsync(nmId, settingsDto, accountId);
            }
            catch (InvalidOperationException ex)
            {
                // Если denominator <= 0 или другая наша проверка – записываем ошибку и пропускаем
                result.CalculationErrors.Add(new PriceCalculationError {
                    NmId = nmId,
                    ErrorMessage = ex.Message
                });
                continue;
            }
            if (price == 0)
                continue;

            payloadData.Add(new {
                nmId     = nmId,
                price    = price,
                discount = settingsDto.PlannedDiscountPercent
            });
        }
        
        result.UploadedCount = payloadData.Count;
        
        if (payloadData.Any())
        {
            var payload = new { data = payloadData };
            string jsonPayload = JsonSerializer.Serialize(payload);

            var content = new StringContent(
                jsonPayload,
                Encoding.UTF8,
                "application/json"
            );

            var response       = await client.PostAsync("/api/v2/upload/task", content);
            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                // Попытаемся распарсить тело ответа
                try
                {
                    var wbError = JsonSerializer.Deserialize<WildberriesApiResponse>(responseString);
                    // Если это наш «уже установлено»…
                    if (wbError != null
                        && wbError.error
                        && wbError.errorText == "The specified prices and discounts are already set")
                    {
                        // просто игнорируем и считаем, что всё ок
                    }
                    else
                    {
                        // любая другая ошибка — кидаем как раньше
                        throw new WildberriesApiException(
                            $"Wildberries API returned error {response.StatusCode}",
                            (int)response.StatusCode,
                            responseString
                        );
                    }
                }
                catch (JsonException)
                {
                    // неудачная десериализация — тоже считаем это «реальной» ошибкой
                    throw new WildberriesApiException(
                        $"Wildberries API returned error {response.StatusCode}",
                        (int)response.StatusCode,
                        responseString
                    );
                }
            }
        }
        return result;
    }
}

public class WildberriesApiResponse
{
    [JsonPropertyName("data")]
    public object? data { get; set; }

    [JsonPropertyName("error")]
    public bool error { get; set; }

    [JsonPropertyName("errorText")]
    public string? errorText { get; set; }
}