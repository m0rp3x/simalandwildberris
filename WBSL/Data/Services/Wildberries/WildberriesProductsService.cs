using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WBSL.Data.Services.Wildberries.Models;
using WBSL.Models;

namespace WBSL.Data.Services.Wildberries;

public record ProductsSyncResult(
    int ProductsCount,
    int ErrorsCount
);

public class WildberriesProductsService : WildberriesBaseService
{
    public List<WbProductCardDto> Cards{ get; } = new List<WbProductCardDto>();
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
        var productsToSave = new List<WbProductCard>();

        do{
            try{
                var (response, cardsCount) = await FetchProductsBatchAsync(updatedAt, nmID);
                totalRequests++;
                totalCards += cardsCount;

                productsToSave.AddRange(response.Cards.Select(card => new WbProductCard{
                    NmID = card.NmID,
                    ImtID = card.ImtID,
                    NmUUID = card.NmUUID,
                    SubjectID = card.SubjectID,
                    SubjectName = card.SubjectName,
                    VendorCode = card.VendorCode,
                    Brand = card.Brand,
                    Title = card.Title,
                    Description = card.Description,
                    NeedKiz = card.NeedKiz,
                    CreatedAt = card.CreatedAt,
                    UpdatedAt = card.UpdatedAt,
                    WbPhotos = card.Photos.Select(p => new WbPhoto{
                        Big = p.Big,
                        C246x328 = p.C246x328,
                        C516x688 = p.C516x688,
                        Hq = p.Hq,
                        Square = p.Square,
                        Tm = p.Tm
                    }).ToList(),
                    SizeChrts = card.Sizes.Select(s => new WbSize{
                        ChrtID = s.ChrtID,
                        TechSize = s.TechSize,
                        WbSize1 = s.WbSize,
                        WbSkus = null
                    }).ToList(),
                    Characteristics = card.Characteristics.Select(ch => new WbCharacteristic{
                        Id = ch.Id,
                        Name = ch.Name,
                        Value = ch.Value.ToString() // Преобразуем object в string
                    }).ToList(),
                    Dimensions = new List<WbDimension>{
                        new WbDimension{
                            Width = card.Dimensions.Width,
                            Height = card.Dimensions.Height,
                            Length = card.Dimensions.Length,
                            WeightBrutto = card.Dimensions.WeightBrutto,
                            IsValid = card.Dimensions.IsValid
                        }
                    }
                }));

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
        } while (ShouldFetchNextBatch(1));

        try{
            await SaveProductsToDatabaseAsync(productsToSave);
        }
        catch (Exception ex){
            exceptions.Add(ex);
            throw new AggregateException("Failed to save products to database", exceptions);
        }

        return new ProductsSyncResult(totalCards, totalRequests);
    }

    private async Task SaveProductsToDatabaseAsync(List<WbProductCard> productsToSave)
{
    if (!productsToSave.Any())
        return;

    await using var transaction = await _db.Database.BeginTransactionAsync();

    try
    {
        // 1️⃣ Собираем ВСЕ характеристики и размеры из productsToSave (уникальные)
        var allCharacteristics = productsToSave
            .SelectMany(p => p.Characteristics)
            .GroupBy(c => c.Id)
            .Select(g => g.First())
            .ToList();

        var allSizes = productsToSave
            .SelectMany(p => p.SizeChrts)
            .GroupBy(s => s.ChrtID)
            .Select(g => g.First())
            .ToList();

        // 2️⃣ Проверяем, какие уже есть в БД
        var existingCharIds = await _db.WbCharacteristics
            .Where(c => allCharacteristics.Select(x => x.Id).Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync();

        var existingSizeIds = await _db.WbSizes
            .Where(s => allSizes.Select(x => x.ChrtID).Contains(s.ChrtID))
            .Select(s => s.ChrtID)
            .ToListAsync();

        // 3️⃣ Добавляем только новые характеристики
        var newCharacteristics = allCharacteristics
            .Where(c => !existingCharIds.Contains(c.Id))
            .ToList();

        if (newCharacteristics.Any())
        {
            await _db.WbCharacteristics.AddRangeAsync(newCharacteristics);
            await _db.SaveChangesAsync(); // Сохраняем, чтобы получить ID
        }

        // 4️⃣ Добавляем только новые размеры
        var newSizes = allSizes
            .Where(s => !existingSizeIds.Contains(s.ChrtID))
            .ToList();

        if (newSizes.Any())
        {
            await _db.WbSizes.AddRangeAsync(newSizes);
            await _db.SaveChangesAsync(); // Сохраняем, чтобы получить ID
        }

        // 5️⃣ Теперь можно безопасно добавлять продукты
        await _db.WbProductCards.AddRangeAsync(productsToSave);
        await _db.SaveChangesAsync();

        await transaction.CommitAsync();
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        throw new Exception("Failed to save products to database", ex);
    }
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