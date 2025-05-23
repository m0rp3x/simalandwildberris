﻿using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Hangfire;
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

    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _syncLocks
        = new ConcurrentDictionary<int, SemaphoreSlim>();

    public WildberriesProductsService(
        PlatformHttpClientFactory httpFactory,
        QPlannerDbContext db, IServiceScopeFactory scopeFactory) : base(httpFactory){
        _db           = db;
        _scopeFactory = scopeFactory;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 60)]
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
        var semaphore = _syncLocks.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        try{
            var syncStartedAt = DateTime.UtcNow;

            using var scope = _scopeFactory.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<QPlannerDbContext>();

            int    totalRequests   = 0;
            int    totalCards      = 0;
            int    fetchErrorCount = 0;
            int    saveErrorCount  = 0;
            string updatedAt       = string.Empty;
            long?  nmID            = null;
            int    totalAvailable  = 1;

            do{
                try{
                    var (response, cardsCount) = await FetchProductsBatchAsync(updatedAt, nmID, accountId);
                    totalRequests++;
                    totalCards += cardsCount;

                    updatedAt      = response.Cursor.UpdatedAt.ToString("o");
                    nmID           = response.Cursor.NmID;
                    totalAvailable = response.Cursor.Total;
                    try{
                        var batch = response.Cards.Select(card => WbProductCardMapToDomain.MapToDomain(card)).ToList();
                        await SaveProductsToDatabaseAsync(db, batch, accountId, syncStartedAt);
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

                        await Task.Delay(3000);
                    }
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

            var toDelete = await db.WbProductCards
                                   .Where(p =>
                                              p.externalaccount_id == accountId
                                              && p.LastSeenAt < syncStartedAt
                                   )
                                   .ToListAsync();
            if (toDelete.Any()){
                db.WbProductCards.RemoveRange(toDelete);
                await db.SaveChangesAsync();
            }

            return new ProductsSyncResult(totalCards, totalRequests);
        }
        finally{
            semaphore.Release();
            if (semaphore.CurrentCount == 1)
                _syncLocks.TryRemove(accountId, out _);
        }
    }

    public async Task SaveProductsToDatabaseAsync(
        QPlannerDbContext db,
        List<WbProductCard> productsToSave,
        int externalAccountId,
        DateTime syncStartedAt){
        if (!productsToSave.Any())
            return;

        await using var transaction = await db.Database.BeginTransactionAsync();

        try{
            await ProcessCharacteristicsAsync(db, productsToSave);
            await ProcessSizesAndSkusAsync(db, productsToSave);

            await UpsertProductCardsAsync(db, productsToSave, externalAccountId, syncStartedAt);

            db.ChangeTracker.Clear();

            await LinkSizesToProductsAsync(db, productsToSave);
            await LinkCharacteristicsAsync(db, productsToSave);
            await transaction.CommitAsync();
            // var existingProductIds = await db.WbProductCards
            //                                  .Where(p => productsToSave.Select(x => x.NmID).Contains(p.NmID))
            //                                  .Select(p => p.NmID)
            //                                  .ToListAsync();
            //
            // var existingEntities = await db.WbProductCards
            //                                // .Include(p => p.WbPhotos)
            //                                .Include(p => p.WbProductCardCharacteristics)
            //                                .Include(p => p.SizeChrts)
            //                                .Where(p => existingProductIds.Contains(p.NmID))
            //                                .ToListAsync();
            //
            // var newProducts = productsToSave
            //                   .Where(p => !existingProductIds.Contains(p.NmID))
            //                   .GroupBy(p => p.NmID)
            //                   .Select(g => g.First())
            //                   .ToList();
            //
            // var existingProducts = productsToSave
            //                        .Where(p => existingProductIds.Contains(p.NmID))
            //                        .ToList();
            //
            // if (newProducts.Any()){
            //     newProducts.ForEach(p => {
            //         p.externalaccount_id = externalAccountId;
            //         p.LastSeenAt         = syncStartedAt;
            //         p.SizeChrts          = new List<WbSize>();
            //         p.WbPhotos           = new List<WbPhoto>();
            //     });
            //     await db.WbProductCards.AddRangeAsync(newProducts);
            // }
            //
            // foreach (var prod in existingProducts){
            //     prod.externalaccount_id = externalAccountId;
            //
            //     var exist = existingEntities.FirstOrDefault(e => e.NmID == prod.NmID);
            //     if (exist == null) continue;
            //
            //     exist.LastSeenAt = syncStartedAt;
            //
            //     db.WbProductCardCharacteristics.RemoveRange(exist.WbProductCardCharacteristics);
            //     exist.WbProductCardCharacteristics = prod.WbProductCardCharacteristics;
            // }
            //
            // await db.SaveChangesAsync();
        }
        catch (Exception ex){
            await transaction.RollbackAsync();
            var msg = $"Не удалось сохранить {productsToSave.Count} товаров " +
                      $"(AccountId={externalAccountId}, SyncAt={syncStartedAt:O}): {ex.Message}";

            throw new InvalidOperationException(msg, ex);
        }
    }

    private async Task LinkCharacteristicsAsync(
        QPlannerDbContext db,
        List<WbProductCard> products){
        var rows = products
                   .SelectMany(p => p.WbProductCardCharacteristics
                                     .Select(pc => new{
                                         p.NmID,
                                         pc.CharacteristicId,
                                         pc.Value
                                     }))
                   .Distinct()
                   .ToList();

        if (!rows.Any()) return;

        // Удаляем старые
        var nmids = rows.Select(r => r.NmID).Distinct().ToList();
        var olds = db.WbProductCardCharacteristics
                     .Where(pc => nmids.Contains(pc.ProductNmID));
        db.WbProductCardCharacteristics.RemoveRange(olds);
        await db.SaveChangesAsync();

        // Вставляем новые
        var newLinks = rows.Select(r => new WbProductCardCharacteristic{
            ProductNmID      = r.NmID,
            CharacteristicId = r.CharacteristicId,
            Value            = r.Value
        }).ToList();

        await db.WbProductCardCharacteristics.AddRangeAsync(newLinks);
        await db.SaveChangesAsync();
    }

    private async Task UpsertProductCardsAsync(
        QPlannerDbContext db,
        List<WbProductCard> productsDto,
        int accountId,
        DateTime syncAt){
        var allNmIds = productsDto.Select(p => p.NmID).ToList();
        var existIds = await db.WbProductCards
                               .Where(p => allNmIds.Contains(p.NmID))
                               .Select(p => p.NmID)
                               .ToListAsync();
        var newEntities = productsDto
                          .Where(p => !existIds.Contains(p.NmID))
                          .Select(dto => new WbProductCard{
                              NmID               = dto.NmID,
                              ImtID              = dto.ImtID,
                              NmUUID             = dto.NmUUID,
                              SubjectID          = dto.SubjectID,
                              SubjectName        = dto.SubjectName,
                              VendorCode         = dto.VendorCode,
                              Brand              = dto.Brand,
                              Title              = dto.Title,
                              Description        = dto.Description,
                              NeedKiz            = dto.NeedKiz,
                              CreatedAt          = dto.CreatedAt,
                              UpdatedAt          = dto.UpdatedAt,
                              externalaccount_id = accountId,
                              LastSeenAt         = syncAt,
                          })
                          .ToList();

        if (newEntities.Any())
            await db.WbProductCards.AddRangeAsync(newEntities);

        var existingEntities = await db.WbProductCards
                                       .Where(p => existIds.Contains(p.NmID))
                                       .ToListAsync();

        foreach (var dto in productsDto.Where(p => existIds.Contains(p.NmID))){
            var ent = existingEntities.First(e => e.NmID == dto.NmID);
            ent.ImtID              = dto.ImtID;
            ent.NmUUID             = dto.NmUUID;
            ent.SubjectID          = dto.SubjectID;
            ent.SubjectName        = dto.SubjectName;
            ent.VendorCode         = dto.VendorCode;
            ent.Brand              = dto.Brand;
            ent.Title              = dto.Title;
            ent.Description        = dto.Description;
            ent.NeedKiz            = dto.NeedKiz;
            ent.UpdatedAt          = dto.UpdatedAt;
            ent.externalaccount_id = accountId;
            ent.LastSeenAt         = syncAt;
        }

        await db.SaveChangesAsync();
    }

    private async Task ProcessSizesAndSkusAsync(
        QPlannerDbContext db,
        List<WbProductCard> products){
        var allSizes = products
                       .SelectMany(p => p.SizeChrts)
                       .GroupBy(s => s.ChrtID)
                       .Select(g => new WbSize{
                           ChrtID = g.Key,
                           Value  = g.First().Value
                       })
                       .ToList();

        if (!allSizes.Any()) return;

        // 1) Возьмём существующие
        var ids = allSizes.Select(s => s.ChrtID).ToList();
        var existing = await db.WbSizes
                               .Where(s => ids.Contains(s.ChrtID))
                               .ToListAsync();

        // 2) Обновим их
        foreach (var ex in existing){
            ex.Value = allSizes.First(s => s.ChrtID == ex.ChrtID).Value;
        }

        // 3) Добавим новые
        var toInsert = allSizes
                       .Where(s => !existing.Any(e => e.ChrtID == s.ChrtID))
                       .ToList();

        if (toInsert.Any())
            await db.WbSizes.AddRangeAsync(toInsert);
    }

    /// <summary>
    /// Добавляет в таблицу WbProductCardSizes (ProductNmID ↔ SizeChrtID) 
    /// только тех связей, которых ещё нет.
    /// </summary>
    private async Task LinkSizesToProductsAsync(
        QPlannerDbContext db,
        List<WbProductCard> incomingProducts){
        var incomingMap = incomingProducts
            .ToDictionary(
                p => p.NmID,
                p => p.SizeChrts.Select(s => s.ChrtID).Distinct().ToList()
            );

        // 2) Собираем все NmID и все ChrtID
        var nmids      = incomingMap.Keys.ToList();
        var allChrtIds = incomingMap.Values.SelectMany(x => x).Distinct().ToList();

        // 3) Загружаем нужные объекты из БД ОДНИМ махом
        var cards = await db.WbProductCards
                            .Where(c => nmids.Contains(c.NmID))
                            .Include(c => c.SizeChrts)
                            .ToListAsync();

        var sizesById = await db.WbSizes
                                .Where(s => allChrtIds.Contains(s.ChrtID))
                                .ToDictionaryAsync(s => s.ChrtID);
        
        foreach (var card in cards){
            card.SizeChrts.Clear();
            
            foreach (var chrtId in incomingMap[card.NmID]){
                if (sizesById.TryGetValue(chrtId, out var size))
                    card.SizeChrts.Add(size);
                // иначе: можно залогировать, что такого размера нет в справочнике
            }
        }

        await db.SaveChangesAsync();
    }


    private async Task ProcessCharacteristicsAsync(
        QPlannerDbContext db,
        List<WbProductCard> products){
        var allChars = products
                       .SelectMany(p => p.WbProductCardCharacteristics)
                       .Select(pc => pc.Characteristic)
                       .Where(c => c != null)
                       .GroupBy(c => c.Id)
                       .Select(g => g.First())
                       .ToList();

        var existingIds = await db.WbCharacteristics
                                  .Where(c => allChars.Select(x => x.Id).Contains(c.Id))
                                  .Select(c => c.Id)
                                  .ToListAsync();

        var toInsert = allChars
                       .Where(c => !existingIds.Contains(c.Id))
                       .ToList();

        if (toInsert.Any()){
            await db.WbCharacteristics.AddRangeAsync(toInsert);
            await db.SaveChangesAsync();
        }

        // Сбрасываем ссылку, чтобы не дублировать в SaveChanges
        foreach (var product in products)
        foreach (var pc in product.WbProductCardCharacteristics)
            pc.Characteristic = null;
    }

    private bool ShouldFetchNextBatch(int totalCardsFetched){
        return Math.Abs(totalCardsFetched) % 100 == 0;
    }

    private async Task<(WbApiResponse Response, int CardsCount)> FetchProductsBatchAsync(string updatedAt, long? nmID,
        int accountId){
        int       attempt       = 0;
        Exception lastException = null;

        while (attempt < maxRetries){
            attempt++;
            try{
                var content  = await CreateRequestContent(updatedAt, nmID);
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