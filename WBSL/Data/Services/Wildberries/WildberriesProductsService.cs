using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WBSL.Data.HttpClientFactoryExt;
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
        PlatformHttpClientFactory httpFactory,
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

                var batch = response.Cards.Select(card => new WbProductCard{
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
                    CreatedAt = NormalizeDateTime(card.CreatedAt),
                    UpdatedAt = NormalizeDateTime(card.UpdatedAt),
                    WbPhotos = card.Photos.Select(p => new WbPhoto{
                        Big = p.Big,
                        C246x328 = p.C246x328,
                        C516x688 = p.C516x688,
                        Hq = p.Hq,
                        Square = p.Square,
                        Tm = p.Tm
                    }).ToList(),
                    SizeChrts = card.Sizes.Select(x => new WbSize{
                        ChrtID = x.ChrtID, TechSize = x.TechSize, WbSize1 = x.WbSize, Value = string.Join(", ", x.Skus),
                    }).ToList(),
                    WbProductCardCharacteristics = card.Characteristics.Select(ch => new WbProductCardCharacteristic{
                        ProductNmID = card.NmID,
                        CharacteristicId = ch.Id,
                        Characteristic = new WbCharacteristic(){
                            Name = ch.Name,
                            Id = ch.Id
                        },
                        Value = ch.Value.ToString(),
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
                });

                await SaveProductsToDatabaseAsync(batch.ToList());

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

        return new ProductsSyncResult(totalCards, totalRequests);
    }

    private static DateTime NormalizeDateTime(DateTime dateTime){
        if (dateTime.Kind == DateTimeKind.Unspecified)
            return DateTime.SpecifyKind(dateTime, DateTimeKind.Local);

        return dateTime.Kind == DateTimeKind.Utc
            ? dateTime.ToLocalTime()
            : dateTime;
    }

    private async Task SaveProductsToDatabaseAsync(List<WbProductCard> productsToSave){
        if (!productsToSave.Any())
            return;

        await using var transaction = await _db.Database.BeginTransactionAsync();

        try{
            var existingProductIds = await _db.WbProductCards
                .Where(p => productsToSave.Select(x => x.NmID).Contains(p.NmID))
                .Select(p => p.NmID)
                .ToListAsync();

            var existingEntities = await _db.WbProductCards
                .Include(p => p.WbPhotos)
                .Include(p => p.WbProductCardCharacteristics)
                .Include(x => x.SizeChrts)
                .Where(p => existingProductIds.Contains(p.NmID))
                .ToListAsync();

            var newProducts = productsToSave
                .Where(p => !existingProductIds.Contains(p.NmID))
                .ToList();

            var existingProducts = productsToSave
                .Where(p => existingProductIds.Contains(p.NmID))
                .ToList();

            await ProcessCharacteristicsAsync(productsToSave);
            await ProcessSizesAndSkusAsync(productsToSave);

            if (newProducts.Any()){
                await _db.WbProductCards.AddRangeAsync(newProducts);
            }

            foreach (var product in existingProducts){
                var existing = existingEntities.FirstOrDefault(p => p.NmID == product.NmID);

                if (existing != null){
                    _db.Entry(existing).CurrentValues.SetValues(product);

                    _db.WbPhotos.RemoveRange(existing.WbPhotos);
                    existing.WbPhotos = product.WbPhotos;

                    _db.WbProductCardCharacteristics.RemoveRange(existing.WbProductCardCharacteristics);
                    existing.WbProductCardCharacteristics = product.WbProductCardCharacteristics;
                }
            }

            await _db.SaveChangesAsync();

            await transaction.CommitAsync();
        }
        catch (Exception ex){
            await transaction.RollbackAsync();
            throw new Exception("Failed to save products to database", ex);
        }
    }

    private async Task ProcessSizesAndSkusAsync(List<WbProductCard> products){
        var allChrtIds = products
            .SelectMany(p => p.SizeChrts)
            .Select(s => s.ChrtID)
            .Distinct()
            .ToList();

        var existingSizes = await _db.WbSizes
            .Where(s => allChrtIds.Contains(s.ChrtID))
            .ToListAsync();

        foreach (var size in products.SelectMany(p => p.SizeChrts))
        {
            var existingSize = existingSizes.FirstOrDefault(s => s.ChrtID == size.ChrtID);
            if (existingSize == null) continue;

            existingSize.Value = size.Value;
            
            size.ChrtID = 0;
        }

        await _db.SaveChangesAsync();
    }

    private async Task ProcessCharacteristicsAsync(List<WbProductCard> products){
        var allCharacteristics = products
            .SelectMany(p => p.WbProductCardCharacteristics)
            .Select(pc => pc.Characteristic)
            .Where(c => c != null)
            .GroupBy(c => c.Id)
            .Select(g => g.First())
            .ToList();

        var existingCharIds = await _db.WbCharacteristics
            .Where(c => allCharacteristics.Select(x => x.Id).Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync();

        var newCharacteristics = allCharacteristics
            .Where(c => !existingCharIds.Contains(c.Id))
            .ToList();

        if (newCharacteristics.Any()){
            await _db.WbCharacteristics.AddRangeAsync(newCharacteristics);
            await _db.SaveChangesAsync();
        }

        foreach (var product in products){
            foreach (var pc in product.WbProductCardCharacteristics){
                pc.Characteristic = null;
            }
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
                var WbClient = await GetWbClientAsync();
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