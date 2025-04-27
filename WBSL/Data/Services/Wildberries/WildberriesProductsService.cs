using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Enums;
using WBSL.Data.Exceptions;
using WBSL.Data.HttpClientFactoryExt;
using WBSL.Data.Mappers;
using WBSL.Models;

namespace WBSL.Data.Services.Wildberries;

public class WildberriesProductsService : WildberriesBaseService
{
    public List<WbProductCardDto> Cards{ get; } = new List<WbProductCardDto>();
    private readonly QPlannerDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly int maxRetries = 3;

    public WildberriesProductsService(
        PlatformHttpClientFactory httpFactory,
        QPlannerDbContext db, IServiceScopeFactory scopeFactory) : base(httpFactory){
        _db = db;
        _scopeFactory = scopeFactory;
    }

    public async Task<List<ProductsSyncResult>> SyncProductsAsync(){
        var externalAccounts = await _db.external_accounts
            .Where(x => x.platform == ExternalAccountType.Wildberries.ToString())
            .ToListAsync();

        var tasks = new List<Task<ProductsSyncResult>>();

        foreach (var account in externalAccounts){
            tasks.Add(SyncProductsForAccountAsync(account.id));
        }

        var results = await Task.WhenAll(tasks);

        return results.ToList();
    }

    private async Task<ProductsSyncResult> SyncProductsForAccountAsync(int accountId){
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QPlannerDbContext>();

        int totalRequests = 0;
        int totalCards = 0;
        int fetchErrorCount = 0;
        int saveErrorCount = 0;
        string updatedAt = string.Empty;
        long? nmID = null;
        int totalAvailable = 1;

        do{
            try{
                var (response, cardsCount) = await FetchProductsBatchAsync(updatedAt, nmID, accountId);
                totalRequests++;
                totalCards += cardsCount;

                try{
                    var batch = response.Cards.Select(card => WbProductCardMapToDomain.MapToDomain(card)).ToList();
                    await SaveProductsToDatabaseAsync(db, batch, accountId);
                }
                catch (Exception saveEx){
                    saveErrorCount++;
                    if (saveErrorCount >= 3){
                        return new ProductsSyncResult(
                            ProductsCount: totalCards,
                            RequestsCount: totalRequests,
                            FetchErrorsCount: fetchErrorCount,
                            SaveErrorsCount: saveErrorCount,
                            IsFatalError: true,
                            FatalErrorMessage: $"Failed to save batches. Error: {saveEx.Message}"
                        );
                    }

                    await Task.Delay(1000);
                    continue;
                }

                updatedAt = response.Cursor.UpdatedAt.ToString("o");
                nmID = response.Cursor.NmID;
                totalAvailable = response.Cursor.Total;
            }
            catch (FetchFailedException fetchEx){
                return new ProductsSyncResult(
                    ProductsCount: totalCards,
                    RequestsCount: totalRequests,
                    FetchErrorsCount: fetchErrorCount + 1,
                    SaveErrorsCount: saveErrorCount,
                    IsFatalError: true,
                    FatalErrorMessage: $"Failed to fetch batch: {fetchEx.Message}"
                );
            }
            catch (Exception ex){
                return new ProductsSyncResult(
                    ProductsCount: totalCards,
                    RequestsCount: totalRequests,
                    FetchErrorsCount: fetchErrorCount,
                    SaveErrorsCount: saveErrorCount,
                    IsFatalError: true,
                    FatalErrorMessage: $"Unhandled exception: {ex.Message}"
                );
            }
        } while (ShouldFetchNextBatch(totalAvailable));

        return new ProductsSyncResult(totalCards, totalRequests);
    }

    public async Task SaveProductsToDatabaseAsync(QPlannerDbContext db,List<WbProductCard> productsToSave, int externalAccountId){
        if (!productsToSave.Any())
            return;

        await using var transaction = await db.Database.BeginTransactionAsync();

        try{
            var existingProductIds = await db.WbProductCards
                .Where(p => productsToSave.Select(x => x.NmID).Contains(p.NmID))
                .Select(p => p.NmID)
                .ToListAsync();

            var existingEntities = await db.WbProductCards
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
                foreach (var product in newProducts){
                    product.externalaccount_id = externalAccountId;
                }
                await db.WbProductCards.AddRangeAsync(newProducts);
            }

            foreach (var product in existingProducts){
                product.externalaccount_id = externalAccountId;
                var existing = existingEntities.FirstOrDefault(p => p.NmID == product.NmID);

                if (existing != null){
                    db.Entry(existing).CurrentValues.SetValues(product);

                    db.WbPhotos.RemoveRange(existing.WbPhotos);
                    existing.WbPhotos = product.WbPhotos;

                    db.WbProductCardCharacteristics.RemoveRange(existing.WbProductCardCharacteristics);
                    existing.WbProductCardCharacteristics = product.WbProductCardCharacteristics;
                }
            }

            await db.SaveChangesAsync();

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

        foreach (var size in products.SelectMany(p => p.SizeChrts)){
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

    private async Task<(WbApiResponse Response, int CardsCount)> FetchProductsBatchAsync(string updatedAt, long? nmID,
        int accountId){
        int attempt = 0;
        Exception lastException = null;

        while (attempt < maxRetries){
            attempt++;
            try{
                var content = await CreateRequestContent(updatedAt, nmID);
                var WbClient = await GetWbClientAsync(accountId, true);
                var response = await WbClient.PostAsync("/content/v2/get/cards/list", content);
                response.EnsureSuccessStatusCode();

                var apiResponse = await response.Content.ReadFromJsonAsync<WbApiResponse>();
                if (apiResponse == null)
                    throw new Exception("API response is null");

                if (apiResponse.Cards == null)
                    throw new Exception("API response cards is null");

                return (apiResponse, apiResponse?.Cards?.Count ?? 0);
            }
            catch (HttpRequestException ex) when (attempt < maxRetries){
                lastException = ex;
                await Task.Delay(CalculateDelay(attempt)); // Экспоненциальная задержка
            }
            catch (Exception ex){
                lastException = ex;
                if (attempt >= maxRetries)
                    break;

                await Task.Delay(CalculateDelay(attempt));
            }
        }

        throw new FetchFailedException($"Failed after {maxRetries} attempts", lastException);
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