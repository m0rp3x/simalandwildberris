using System.Net;
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
        var nmIds = await _priceCalculator.PrepareCalculationDataAsync(accountId, settingsDto.WildberriesCategoryId);

        var payloadData = new List<object>();
        foreach (var nmId in nmIds){
            decimal price;
            try{
                price = await _priceCalculator
                    .CalculatePriceAsync(nmId, settingsDto, accountId);
            }
            catch (InvalidOperationException ex){
                result.Errors.Add(new BatchError{
                    BatchIndex = -1,
                    BatchSize = 1,
                    StatusCode = -1,
                    ErrorText = $"Calculation error for NmId={nmId}: {ex.Message}"
                });
                continue;
            }

            if (price == 0)
                continue;

            int roundedPrice = (int)Math.Ceiling(price); 
            payloadData.Add(new {
                nmId     = nmId,
                price    = roundedPrice,
                discount = settingsDto.PlannedDiscountPercent
            });
        }

        result.TotalCount = payloadData.Count;
        if (result.TotalCount == 0)
            return result;

        var batches = payloadData
            .Select((item, idx) => new{ item, idx })
            .GroupBy(x => x.idx / 100)
            .Select(g => g.Select(x => x.item).ToList())
            .ToList();

        const int MaxParallelism = 5;
        using var semaphore = new SemaphoreSlim(MaxParallelism);

        // 4. Запускаем обработки пачек
        var batchTasks = batches
            .Select((batch, batchIndex) => ProcessBatchAsync(batchIndex, batch))
            .ToList();

        var batchResults = await Task.WhenAll(batchTasks);

        var errors = batchResults.Where(e => e != null)!.Select(e => e!).ToList();
        result.Errors = errors;

        var failedCount = errors.Sum(e => e.BatchSize);
        result.FailedCount = failedCount;
        result.SuccessCount = result.TotalCount - failedCount;

        return result;

        async Task<BatchError?> ProcessBatchAsync(int batchIndex, List<object> batch){
            await semaphore.WaitAsync();
            try{
                var wrapper = new{ data = batch };
                var json = JsonSerializer.Serialize(wrapper);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response;
                string body;
                try{
                    response = await client.PostAsync("/api/v2/upload/task", content);
                    body = await response.Content.ReadAsStringAsync();
                }
                catch (Exception ex){
                    // сетевой сбой или прочая критическая ошибка
                    return new BatchError{
                        BatchIndex = batchIndex,
                        BatchSize = batch.Count,
                        StatusCode = -1,
                        ErrorText = $"Exception: {ex.GetType().Name}: {ex.Message}"
                    };
                }

                if (response.IsSuccessStatusCode){
                    // успех — возвращаем null
                    return null;
                }
                else{
                    // пытаемся вытащить текст ошибки из JSON
                    string errorText;
                    try{
                        var wbError = JsonSerializer
                            .Deserialize<WildberriesApiResponse>(body);
                        errorText = wbError != null && wbError.error
                            ? wbError.errorText
                            : body;
                    }
                    catch{
                        errorText = body;
                    }

                    // специальный кейс: «уже установлено» считаем успехом
                    if (response.StatusCode == HttpStatusCode.BadRequest
                        && errorText.Contains("The specified prices and discounts are already set")){
                        return null;
                    }

                    // реальная ошибка
                    return new BatchError{
                        BatchIndex = batchIndex,
                        BatchSize = batch.Count,
                        StatusCode = (int)response.StatusCode,
                        ErrorText = errorText
                    };
                }
            }
            finally{
                semaphore.Release();
            }
        }
    }
}

public class WildberriesApiResponse
{
    [JsonPropertyName("data")] public object? data{ get; set; }

    [JsonPropertyName("error")] public bool error{ get; set; }

    [JsonPropertyName("errorText")] public string? errorText{ get; set; }
}