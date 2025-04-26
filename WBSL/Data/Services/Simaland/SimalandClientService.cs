using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using WBSL.Models;

namespace WBSL.Data.Services.Simaland;

public class SimalandClientService
{
    private readonly QPlannerDbContext _db;

    public SimalandClientService(
        QPlannerDbContext db){
        _db = db;
    }

    private class ProductInfo
    {
        public long Sid{ get; set; }
        public int Balance{ get; set; }
    }

    public async Task FetchAndSaveProductsBalance(List<long> sids,
        HttpClient client,
        HttpClient wildberriesClient,
        CancellationToken ct,
        int warehouseId,
        List<int> externalAccountIds){
        const int batchSize = 1000;
        const int maxParallelRequests = 5; // 5 параллельных запроса
        const int delayBetweenRequestsMs = 50; // Задержка между партиями
        const int maxRetryAttempts = 3; // Макс. количество попыток
        const int retryDelayMs = 1000; // Задержка между попытками (1 сек)

        var bag = new ConcurrentBag<ProductInfo>();
        var processedCount = 0;

        var options = new ParallelOptions{
            MaxDegreeOfParallelism = maxParallelRequests, CancellationToken = ct
        };

        await Parallel.ForEachAsync(sids.Distinct(), options, async (id, token) => {
            int retryCount = 0;
            bool success = false;
            while (retryCount < maxRetryAttempts && !success){
                try{
                    var response = await client.GetAsync($"item/?sid={id}", ct);
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

                    if (item.TryGetProperty("balance", out var balance)){
                        bag.Add(new ProductInfo{
                            Sid = id,
                            Balance = balance.GetInt32()
                        });

                        Interlocked.Increment(ref processedCount);
                        success = true;
                    }

                    if (bag.Count >= batchSize){
                        await SaveBatchAndPushToWB(bag.ToList(), wildberriesClient, ct, warehouseId, externalAccountIds);
                        bag = new ConcurrentBag<ProductInfo>();
                    }

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
        if (!bag.IsEmpty)
            await SaveBatchAndPushToWB(bag.ToList(), wildberriesClient, ct, warehouseId, externalAccountIds);
    }

    private async Task SaveBatchAndPushToWB(List<ProductInfo> batch, 
        HttpClient wbClient, 
        CancellationToken ct,
        int warehouseId,
        List<int> externalAccountIds){
        try{
            var toUpdate = batch.Select(pi => new product{ sid = pi.Sid, balance = pi.Balance }).ToList();
            var bulk = new BulkConfig{
                UpdateByProperties = new List<string>{ nameof(product.sid) },
                PropertiesToInclude = new List<string>{ nameof(product.balance) },
                SetOutputIdentity = false,
                PreserveInsertOrder = false,
                UseTempDB = false,
            };
            await _db.BulkUpdateAsync(toUpdate, bulk, cancellationToken: ct);
            await _db.SaveChangesAsync(ct);

            Console.WriteLine($"Saved batch of {toUpdate.Count} products");

            await UpdateStocksOnWildberries(batch, wbClient, ct, warehouseId, externalAccountIds);
        }
        catch (Exception ex){
            Console.WriteLine($"Error saving batch: {ex.Message}");
        }
    }

    private async Task UpdateStocksOnWildberries(List<ProductInfo> batch, HttpClient wbClient, CancellationToken ct, int warehouseId, List<int> externalAccountIds){
        try{
            var productSids = batch.Select(p => p.Sid).ToList();

            var products = await _db.products
                .AsNoTracking()
                .Where(p => productSids.Contains(p.sid))
                .ToListAsync(cancellationToken: ct);
            
            var productVendorCodes = productSids.Select(sid => sid.ToString()).ToList();

            var wbProductCards = await _db.WbProductCards
                .AsNoTracking()
                .Include(x => x.SizeChrts)
                .Where(wb => productVendorCodes.Contains(wb.VendorCode) 
                             && wb.externalaccount_id.HasValue 
                             && externalAccountIds.Contains(wb.externalaccount_id.Value))
                .ToListAsync(cancellationToken: ct);

            var stocks = new List<object>();

            foreach (var pi in batch){
                var prod = products.FirstOrDefault(p => p.sid == pi.Sid);
                if (prod == null) continue;

                var wbCard = wbProductCards.FirstOrDefault(wb => wb.VendorCode == prod.sid.ToString());
                if (wbCard == null) continue;

                var lots = prod.qty_multiplier > 0 ? pi.Balance / prod.qty_multiplier : 0;

                if (lots < 0) continue;

                var firstSize = wbCard.SizeChrts.FirstOrDefault();
                if (firstSize == null || string.IsNullOrWhiteSpace(firstSize.Value)) continue;

                var skuParts = firstSize.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var sku = skuParts.FirstOrDefault();
                if (string.IsNullOrEmpty(sku)) continue;

                stocks.Add(new
                {
                    sku = sku,
                    amount = lots
                });
            }

            if (stocks.Count == 0){
                Console.WriteLine("No stocks to update to Wildberries.");
                return;
            }

            var payload = new{
                stocks = stocks
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            

            var response = await wbClient.PutAsync($"/api/v3/stocks/{warehouseId}", content, ct);

            if (response.IsSuccessStatusCode){
                Console.WriteLine($"Successfully updated {stocks.Count} stocks to Wildberries.");
            }
            else{
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                Console.WriteLine($"Failed to update stocks to Wildberries: {response.StatusCode} {errorContent}");
            }
        }
        catch (Exception ex){
            Console.WriteLine($"Error updating stocks to Wildberries: {ex.Message}");
        }
    }
}