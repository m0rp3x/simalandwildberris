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
        QPlannerDbContext db)
    {
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
        CancellationToken ct)
    {
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
                        await SaveBatchAndPushToWB(bag.ToList(), wildberriesClient, ct);
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
            await SaveBatchAndPushToWB(bag.ToList(), wildberriesClient, ct);
    }

    private async Task SaveBatchAndPushToWB(List<ProductInfo> batch, HttpClient wbClient, CancellationToken ct){
        try{
            var toUpdate = batch.Select(pi => new product { sid = pi.Sid, balance = pi.Balance }).ToList();
            var bulk = new BulkConfig { UpdateByProperties = new List<string>{ nameof(product.sid) },
                PropertiesToInclude = new List<string>{ nameof(product.balance) },
                SetOutputIdentity = false,
                PreserveInsertOrder = false,
                UseTempDB = false, };
            await _db.BulkUpdateAsync(toUpdate, bulk, cancellationToken: ct);
            await _db.SaveChangesAsync(ct);
            
            Console.WriteLine($"Saved batch of {toUpdate.Count} products");
            
            await UpdateStocksOnWildberries(batch, wbClient, ct);
        }
        catch (Exception ex){
            Console.WriteLine($"Error saving batch: {ex.Message}");
        }
    }
    private async Task UpdateStocksOnWildberries(List<ProductInfo> batch, HttpClient wbClient, CancellationToken ct)
    {
        var wbTasks = new List<Task>();

        foreach (var pi in batch)
        {
            try
            {
                var prod = await _db.products.FirstAsync(p => p.sid == pi.Sid, cancellationToken: ct);
                var lots = prod.qty_multiplier > 0 ? pi.Balance / prod.qty_multiplier : 0; // Защита от деления на 0

                if (lots <= 0)
                {
                    Console.WriteLine($"Product {prod.sid} has 0 available lots, skipping update to WB.");
                    continue;
                }

                var content = new StringContent(JsonSerializer.Serialize(new
                {
                    nmId = prod.sid,
                    quantity = lots
                }), Encoding.UTF8, "application/json");

                wbTasks.Add(wbClient.PostAsync("/api/v1/stocks", content, ct));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to prepare update for SID {pi.Sid}: {ex.Message}");
            }
        }

        if (wbTasks.Count > 0)
        {
            try
            {
                await Task.WhenAll(wbTasks);
                Console.WriteLine($"Pushed {wbTasks.Count} stocks updates to Wildberries.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending stocks to Wildberries: {ex.Message}");
            }
        }
    }
}