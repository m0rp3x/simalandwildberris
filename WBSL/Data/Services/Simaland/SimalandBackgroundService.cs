using System.Collections.Concurrent;
using System.Text.Json;
using EFCore.BulkExtensions;
using Hangfire;
using Hangfire.Server;
using WBSL.Data.Handlers;
using WBSL.Data.HttpClientFactoryExt;
using WBSL.Models;

namespace WBSL.Data.Services.Simaland;

public class SimalandBackgroundService : SimalandBaseService
{
    private readonly QPlannerDbContext _db;

    public SimalandBackgroundService(PlatformHttpClientFactory factory, QPlannerDbContext db) : base(factory){
        _db = db;
    }

    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task<bool> SyncProductsBalanceAsync(){
        try{
            var ids = _db.products.Select(x => x.sid).ToList();

            if (ids.Count == 0){
                return true;
            }
            var сlient = await GetClientAsync(2);
            var result = await FetchAndSaveProductsBalance(ids, сlient);

            if (!result){
                throw new Exception("Temporary sync failure");
            }

            return true;
        }
        catch (CircuitBrokenException ex){
            Console.WriteLine($"API blocked: {ex.Message}");
            throw new JobPerformanceException("API is down", ex);
        }
        catch (OperationCanceledException){
            Console.WriteLine("SyncProductsAsync job was canceled");
            throw;
        }
        catch (Exception ex) when (IsTransientError(ex)){
            Console.WriteLine("Transient error in SyncProductsAsync");
            throw;
        }
        catch (Exception ex){
            Console.WriteLine("Fatal error in SyncProductsAsync");
            return false;
        }
    }

    private bool IsTransientError(Exception ex){
        return ex is TimeoutException
               || ex is HttpRequestException
               || (ex.InnerException != null && IsTransientError(ex.InnerException));
    }

    private class ProductInfo
    {
        public long Sid{ get; set; }
        public int Balance{ get; set; }
    }

    private async Task<bool> FetchAndSaveProductsBalance(List<long> ids, HttpClient client){
        const int batchSize = 1000;
        const int maxParallelRequests = 5; // 5 параллельных запроса
        const int delayBetweenRequestsMs = 50; // Задержка между партиями
        const int maxRetryAttempts = 3; // Макс. количество попыток
        const int retryDelayMs = 1000; // Задержка между попытками (1 сек)

        var productsBatch = new ConcurrentBag<ProductInfo>();
        var lastSaveTime = DateTime.Now;
        var processedCount = 0;

        var options = new ParallelOptions{
            MaxDegreeOfParallelism = maxParallelRequests
        };

        await Parallel.ForEachAsync(ids.Distinct(), options, async (id, ct) => {
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
                        productsBatch.Add(new ProductInfo{
                            Sid = id,
                            Balance = balance.GetInt32()
                        });

                        Interlocked.Increment(ref processedCount);
                        Console.WriteLine($"Processed: {processedCount} | Last saved: {lastSaveTime:HH:mm:ss}");
                        
                        success = true; 
                    }

                    if (productsBatch.Count >= batchSize){
                        await SaveBatchToDatabase(productsBatch);
                        productsBatch = new ConcurrentBag<ProductInfo>();
                        lastSaveTime = DateTime.Now;
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
        if (productsBatch.Count > 0)
        {
            await SaveBatchToDatabase(productsBatch);
        }
        
        return true;
    }

    private async Task SaveBatchToDatabase(ConcurrentBag<ProductInfo> productsBatch){
        if (productsBatch.IsEmpty) return;

        try{
            var batchList = productsBatch.ToList();

            var productsToUpdate = batchList.Select(productInfo => new product{
                sid = productInfo.Sid,
                balance = productInfo.Balance,
            }).ToList();

            var bulkConfig = new BulkConfig{
                UpdateByProperties = new List<string>{ nameof(product.sid) },
                PropertiesToInclude = new List<string>{ nameof(product.balance) },
                SetOutputIdentity = false,
                PreserveInsertOrder = false,
                UseTempDB = false,
            };

            await _db.BulkUpdateAsync(productsToUpdate, bulkConfig);
            await _db.SaveChangesAsync();

            Console.WriteLine($"Saved batch of {batchList.Count} products");
        }
        catch (Exception ex){
            Console.WriteLine($"Error saving batch: {ex.Message}");
        }
    }
}