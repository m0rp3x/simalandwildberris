using System.Net;
using System.Text;
using System.Text.Json;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Enums;
using WBSL.Data.HttpClientFactoryExt;
using WBSL.Models;

namespace WBSL.Data.Services.Simaland;

public class SimalandClientService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public SimalandClientService(
        QPlannerDbContext db, IServiceScopeFactory scopeFactory){
        _scopeFactory = scopeFactory;
    }

    public async Task<(List<ProductInfo> Successful, int ProcessedCount, List<FailedStock> Failed)>
        FetchAndSaveProductsBalance(List<long> sids,
                                    HttpClient client,
                                    HttpClient wildberriesClient,
                                    CancellationToken ct,
                                    int warehouseId,
                                    List<int> externalAccountIds){
        const int batchSize              = 1000;
        const int maxParallelRequests    = 20; // 5 параллельных запроса
        const int delayBetweenRequestsMs = 50; // Задержка между партиями
        const int maxRetryAttempts       = 3; // Макс. количество попыток
        const int retryDelayMs           = 1000; // Задержка между попытками (1 сек)

        var allSuccessful  = new List<ProductInfo>();
        var allFailed      = new List<FailedStock>();
        int totalProcessed = 0;

        var batchList   = new List<ProductInfo>();
        var lockBatch   = new object();
        var lockResults = new object();

        var options = new ParallelOptions{
            MaxDegreeOfParallelism = maxParallelRequests, CancellationToken = ct
        };

        var uniqueSids = sids;
        await Parallel.ForEachAsync(uniqueSids, options, async (id, token) => {
            int  retryCount = 0;
            bool success    = false;
            while (retryCount < maxRetryAttempts && !success){
                try{
                    var response = await client.GetAsync($"item/?sid={id}&expand=stocks,min_qty", ct);
                    if (!response.IsSuccessStatusCode){
                        Console.WriteLine($"Error fetching product {id}: Server returned status {response.StatusCode}");
                        return;
                    }

                    var json = await response.Content.ReadAsStringAsync(ct);
                    var root = JsonSerializer.Deserialize<JsonElement>(json);

                    // Получаем массив items
                    var items = root.GetProperty("items");
                    if (items.GetArrayLength() == 0) return;

                    var item = items[0];
                    if (!item.TryGetProperty("balance", out var balElem)) return;

                    var pi = new ProductInfo{
                        Sid     = id,
                        Balance = balElem.GetInt32(),
                        qty_multiplier = item.TryGetProperty("min_qty", out var mq)
                            ? mq.GetInt32()
                            : 1
                    };

                    List<ProductInfo> toFlush = null;

                    lock (lockBatch){
                        batchList.Add(pi);
                        if (batchList.Count >= batchSize){
                            toFlush = batchList.ToList();
                            batchList.Clear();
                        }
                    }

                    if (toFlush != null){
                        var batchResult = await SaveBatchAndPushToWB(
                            toFlush,
                            wildberriesClient,
                            ct,
                            warehouseId,
                            externalAccountIds);

                        // 3) Аккумулируем результаты под lockResults
                        lock (lockResults){
                            allSuccessful.AddRange(batchResult.Successful);
                            allFailed.AddRange(batchResult.Failed);
                            totalProcessed += toFlush.Count;
                        }
                    }

                    success = true;

                    await Task.Delay(delayBetweenRequestsMs, ct);
                }
                catch (Exception ex){
                    Console.WriteLine(
                        $"Error fetching product {id} (attempt {retryCount + 1}/{maxRetryAttempts}): {ex.Message}");
                    retryCount++;
                    await Task.Delay(retryDelayMs, ct);
                }
            }
        });
        List<ProductInfo> lastFlush = null;
        lock (lockBatch){
            if (batchList.Count > 0){
                lastFlush = batchList.ToList();
                batchList.Clear();
            }
        }

        if (lastFlush != null){
            var batchResult = await SaveBatchAndPushToWB(
                lastFlush,
                wildberriesClient,
                ct,
                warehouseId,
                externalAccountIds);

            lock (lockResults){
                allSuccessful.AddRange(batchResult.Successful);
                allFailed.AddRange(batchResult.Failed);
                totalProcessed += lastFlush.Count;
            }
        }

        return (Successful: allSuccessful, ProcessedCount: totalProcessed, Failed: allFailed);
    }

    public async Task<int> ResetBalancesInWbAsync(int externalAccountId, CancellationToken ct){
        using var scope   = _scopeFactory.CreateScope();
        var       db      = scope.ServiceProvider.GetRequiredService<QPlannerDbContext>();
        var       factory = scope.ServiceProvider.GetRequiredService<PlatformHttpClientFactory>();

        var warehouseId = await db.external_accounts
                                  .Where(x => x.id == externalAccountId)
                                  .Select(x => x.warehouseid)
                                  .FirstOrDefaultAsync(ct);

        var accountIds = await db.external_accounts
                                 .Where(x => x.warehouseid == warehouseId)
                                 .Select(x => x.id)
                                 .ToListAsync(ct);

        var wbCards = await db.WbProductCards
                              .Include(c => c.SizeChrts)
                              .Where(c => c.externalaccount_id.HasValue
                                          && accountIds.Contains(c.externalaccount_id.Value))
                              .ToListAsync(ct);

        if (wbCards.Count == 0)
            return 0;

        var wbClient = await factory.CreateClientAsync(
            ExternalAccountType.WildBerriesMarketPlace,
            externalAccountId);

        const int batchSize = 1000;
        var       totalSent = 0;
        for (int i = 0; i < wbCards.Count; i += batchSize){
            var batch = wbCards
                        .Skip(i)
                        .Take(batchSize)
                        .ToList();

            var stocks = new List<object>();

            foreach (var card in batch){
                var firstSize = card.SizeChrts
                                    .Select(sc => sc.Value)
                                    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                if (firstSize == null)
                    continue;

                var sku = firstSize
                          .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                          .FirstOrDefault();
                if (string.IsNullOrEmpty(sku))
                    continue;

                stocks.Add(new{
                    sku    = sku,
                    amount = 0
                });
            }

            if (stocks.Count == 0){
                Console.WriteLine("No stocks to update to Wildberries in this batch.");
                continue; // не return, чтобы пройти все батчи
            }

            var payload = new{ stocks };
            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"
            );

            HttpResponseMessage response;
            try{
                response = await wbClient.PutAsync(
                    $"/api/v3/stocks/{warehouseId}",
                    content,
                    ct
                );
            }
            catch (Exception ex){
                Console.WriteLine($"Error sending batch starting at index {i}: {ex}");
                continue;
            }

            if (response.IsSuccessStatusCode)
                Console.WriteLine($"Successfully updated {stocks.Count} stocks as zero.");
            else{
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                Console.WriteLine($"Failed to update stocks: {response.StatusCode} {errorContent}");
            }

            totalSent += stocks.Count;
        }

        return totalSent;
    }

    private async Task<StockUpdateResult> SaveBatchAndPushToWB(List<ProductInfo> batch,
                                                               HttpClient wbClient,
                                                               CancellationToken ct,
                                                               int warehouseId,
                                                               List<int> externalAccountIds){
        try{
            var saveTask = Task.Run(async () => {
                var toUpdate = batch.Select(pi => new product{ sid = pi.Sid, balance = pi.Balance }).ToList();

                var bulk = new BulkConfig{
                    UpdateByProperties  = new List<string>{ nameof(product.sid) },
                    PropertiesToInclude = new List<string>{ nameof(product.balance) },
                    SetOutputIdentity   = false,
                    PreserveInsertOrder = false,
                    UseTempDB           = false,
                };

                using var scope = _scopeFactory.CreateScope();
                var       db    = scope.ServiceProvider.GetRequiredService<QPlannerDbContext>();

                await db.BulkUpdateAsync(toUpdate, bulk, cancellationToken: ct);
                await db.SaveChangesAsync(ct);
                Console.WriteLine($"[DB] Saved batch of {toUpdate.Count} products");
            }, ct);

            var pushTask = Task.Run(async () => {
                var result = await UpdateStocksOnWildberries(
                    batch, wbClient, ct, warehouseId, externalAccountIds);
                Console.WriteLine(
                    $"[WB] Pushed batch of {batch.Count} products: " +
                    $"Succeeded={result.Successful.Count}, Failed={result.Failed.Count}");
                return result;
            }, ct);
            await Task.WhenAll(saveTask, pushTask);

            // 4) Возвращаем результат пуша
            return pushTask.Result;
        }
        catch (Exception ex){
            Console.WriteLine($"Error saving batch: {ex.Message}");
            return new StockUpdateResult();
        }
    }

    private async Task<StockUpdateResult> UpdateStocksOnWildberries(List<ProductInfo> batch, HttpClient wbClient,
                                                                    CancellationToken ct,
                                                                    int warehouseId, List<int> externalAccountIds){
        var successfulProducts = new List<ProductInfo>();
        var result             = new StockUpdateResult();
        try{
            using var scope = _scopeFactory.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<QPlannerDbContext>();

            var productVendorCodes = batch.Select(sid => sid.Sid.ToString()).ToList();

            var wbProductCards = await db.WbProductCards
                                         .AsNoTracking()
                                         .Include(x => x.SizeChrts)
                                         .Where(wb => productVendorCodes.Contains(wb.VendorCode)
                                                      && wb.externalaccount_id.HasValue
                                                      && externalAccountIds.Contains(wb.externalaccount_id.Value))
                                         .ToListAsync(cancellationToken: ct);

            var cardsByVendor = wbProductCards.ToDictionary(wb => wb.VendorCode);

            var stocks = new List<(string Sku, int Amount)>();

            foreach (var prod in batch){
                var sidStr = prod.Sid.ToString();
                if (!cardsByVendor.TryGetValue(sidStr, out var wbCard))
                    continue;

                var lots = prod.qty_multiplier > 0 ? prod.Balance / prod.qty_multiplier : 0;

                if (lots < 0) continue;

                var amountToSend = prod.Balance >= 3
                    ? lots
                    : 0;
                var firstSize = wbCard.SizeChrts.FirstOrDefault();
                if (firstSize == null || string.IsNullOrWhiteSpace(firstSize.Value)) continue;

                var skuParts = firstSize.Value.Split(',',
                                                     StringSplitOptions.RemoveEmptyEntries |
                                                     StringSplitOptions.TrimEntries);
                var sku = skuParts.FirstOrDefault();
                if (string.IsNullOrEmpty(sku)) continue;

                stocks.Add((sku, amountToSend));
                successfulProducts.Add(prod);
            }

            if (!stocks.Any()){
                Console.WriteLine("No stocks to update to Wildberries.");
                return result;
            }

            HttpContent ToContent(IEnumerable<(string Sku, int Amount)> list) =>
                new StringContent(
                    JsonSerializer.Serialize(new{ stocks = list.Select(x => new{ sku = x.Sku, amount = x.Amount }) }),
                    Encoding.UTF8,
                    "application/json");

            var content  = ToContent(stocks);
            var response = await wbClient.PutAsync($"/api/v3/stocks/{warehouseId}", content, ct);

            if (response.IsSuccessStatusCode){
                result.Successful.AddRange(batch);
                return result;
            }

            if (response.StatusCode == HttpStatusCode.Conflict){
                var           errJson = await response.Content.ReadAsStringAsync(ct);
                List<WbError> errors;
                try{
                    errors = JsonSerializer.Deserialize<List<WbError>>(errJson)
                             ?? throw new JsonException("WB error response is null or not an array");
                }
                catch (JsonException){
                    Console.WriteLine($"Cannot parse WB error: {errJson}");
                    return result;
                }

                // SKU-ши, которые упали
                var err        = errors.FirstOrDefault();
                var failedSkus = err.data.Select(d => d.sku).ToHashSet();

                // Записываем «неотправленных» в результат
                foreach (var d in err.data){
                    result.Failed.Add(new FailedStock{
                        Sku          = d.sku,
                        Amount       = d.amount,
                        ErrorCode    = err.code,
                        ErrorMessage = err.message
                    });
                }

                // Оставляем только те, что прошли
                var retryStocks = stocks
                                  .Where(x => !failedSkus.Contains(x.Sku))
                                  .ToList();

                if (!retryStocks.Any()){
                    // Нечего больше отправлять
                    return result;
                }

                // Пробуем ещё раз
                var content2  = ToContent(retryStocks);
                var response2 = await wbClient.PutAsync($"/api/v3/stocks/{warehouseId}", content2, ct);

                if (response2.IsSuccessStatusCode){
                    // Если второй раз ок — все эти retryStocks считаем успешными
                    // Надо найти оригинальные ProductInfo по sku и добавить в result.Successful
                    var skuSet = retryStocks.Select(x => x.Sku).ToHashSet();
                    result.Successful.AddRange(batch.Where(p => skuSet.Contains(p.Sid.ToString())));
                }
                else{
                    var errorText2 = await response2.Content.ReadAsStringAsync(ct);
                    // Для каждого SKU из retryStocks добавляем в Failed
                    foreach (var (Sku, Amount) in retryStocks){
                        result.Failed.Add(new FailedStock{
                            Sku          = Sku,
                            Amount       = Amount,
                            ErrorCode    = response2.StatusCode.ToString(),
                            ErrorMessage = errorText2
                        });
                    }

                    Console.WriteLine($"Retry failed with {response2.StatusCode}");
                }

                return result;
            }

            // Другие ошибки
            var errStocks = stocks;
            var errTxt    = await response.Content.ReadAsStringAsync(ct);
            Console.WriteLine($"Failed to update stocks to Wildberries: {response.StatusCode} {errTxt}");
            foreach (var (Sku, Amount) in errStocks){
                result.Failed.Add(new FailedStock{
                    Sku          = Sku,
                    Amount       = Amount,
                    ErrorCode    = response.StatusCode.ToString(),
                    ErrorMessage = errTxt
                });
            }

            return result;
        }
        catch (Exception ex){
            Console.WriteLine($"Error updating stocks to Wildberries: {ex.Message}");
            foreach (var prod in batch){
                result.Failed.Add(new FailedStock{
                    Sku          = prod.Sid.ToString(),
                    Amount       = 0,
                    ErrorCode    = "Exception",
                    ErrorMessage = ex.Message
                });
            }

            return result;
        }
    }

    public class WbError
    {
        public List<WbErrorData> data{ get; set; } = new();
        public string code{ get; set; }
        public string message{ get; set; }
    }

    public class WbErrorData
    {
        public string sku{ get; set; }
        public int amount{ get; set; }
    }

    public class StockUpdateResult
    {
        public List<ProductInfo> Successful{ get; set; } = new();
        public List<FailedStock> Failed{ get; set; } = new();
    }
}