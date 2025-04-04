using System.Text;
using System.Text.Json;
using WBSL.Data.Services.Wildberries.Models;

namespace WBSL.Data.Services.Wildberries;

public record ProductsSyncResult(
    int ProductsCount,
    int ErrorsCount
);

public class WildberriesProductsService : WildberriesBaseService
{
    public List<WbProductCard> Cards{ get; } = new List<WbProductCard>();
    private readonly QPlannerDbContext _db;
    private readonly int maxRetries = 3;

    public WildberriesProductsService(
        IHttpClientFactory httpFactory,
        QPlannerDbContext db) : base(httpFactory){
        _db = db;
    }

    public async Task<ProductsSyncResult> SyncProductsAsync(){
        int totalRequests = 0;
        int totalCards = 0;
        string updatedAt = string.Empty;
        long? nmID = null;
        var exceptions = new List<Exception>();

        do{
            try{
                var (response, cardsCount) = await FetchProductsBatchAsync(updatedAt, nmID);
                totalRequests++;
                totalCards += cardsCount;

                updatedAt = response.Cursor.UpdatedAt.ToString("o");
                nmID = response.Cursor.NmID;
            }
            catch (Exception ex){
                exceptions.Add(ex);
                if (exceptions.Count >= 3) // Максимум 3 ошибки подряд
                {
                    throw new AggregateException("Failed after multiple retries", exceptions);
                }

                await Task.Delay(1000);
            }
        } while (ShouldFetchNextBatch(totalCards));

        return new ProductsSyncResult(totalCards, totalRequests);
    }

    private bool ShouldFetchNextBatch(int totalCardsFetched){
        return Math.Abs(totalCardsFetched) % 100 == 0;
    }

    private async Task<(WbApiResponse Response, int CardsCount)> FetchProductsBatchAsync(string updatedAt, long? nmID){
        int attempt = 0;
        Exception lastException = null;

        while (attempt < maxRetries){
            attempt++;
            try{
                var content = await CreateRequestContent(updatedAt, nmID);
                var response = await WbClient.PostAsync("/content/v2/get/cards/list", content);
                response.EnsureSuccessStatusCode();

                var apiResponse = await response.Content.ReadFromJsonAsync<WbApiResponse>();

                if (apiResponse?.Cards != null){
                    Cards.AddRange(apiResponse.Cards);
                }

                return (apiResponse, apiResponse?.Cards?.Count ?? 0);
            }
            catch (HttpRequestException ex) when (attempt < maxRetries){
                lastException = ex;
                await Task.Delay(CalculateDelay(attempt)); // Экспоненциальная задержка
            }
            catch (Exception ex){
                lastException = ex;
                break;
            }
        }

        throw new Exception($"Failed after {maxRetries} attempts", lastException);
    }

    private TimeSpan CalculateDelay(int attempt){
        return TimeSpan.FromSeconds(Math.Pow(2, attempt));
    }

    private async Task<StringContent>
        CreateRequestContent(string updatedAt = "", long? nmID = null, string? textSearch = null){
        var cursor = new Dictionary<string, object>{
            ["limit"] = 100
        };

        if (!string.IsNullOrEmpty(updatedAt)){
            cursor["updatedAt"] = updatedAt;
        }

        if (nmID.HasValue){
            cursor["nmID"] = nmID.Value;
        }

        var filter = new Dictionary<string, object>{
            ["withPhoto"] = -1
        };
        if (!string.IsNullOrWhiteSpace(textSearch)){
            filter["textSearch"] = textSearch;
        }

        var requestData = new Dictionary<string, object>{
            ["settings"] = new{
                cursor = cursor,
                filter = filter
            }
        };

        var json = JsonSerializer.Serialize(requestData);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }
}