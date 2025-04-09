using System.Collections.Concurrent;
using System.Text.Json;
using Hangfire;
using Hangfire.Server;
using Microsoft.EntityFrameworkCore;
using WBSL.Data.Enums;
using WBSL.Data.Handlers;
using WBSL.Data.HttpClientFactoryExt;

namespace WBSL.Data.Services.Simaland;

public class SimalandService : SimalandBaseService
{
    private readonly QPlannerDbContext _db;

    public SimalandService(PlatformHttpClientFactory factory, QPlannerDbContext db) : base(factory){
        _db = db;
    }

    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task<bool> SyncProductsBalanceAsync(){
        //TODO: тут типо фетч + сохранение в бд]
        //Подстроить чтобы админа брался token сималанд, и по нему запрос по фетчу наличия продуктов
        try{
            var Сlient = await GetClientAsync(2);
            
            var ids = _db.products.Select(x => x.sid).ToList();
            
            var result = await FetchAndSaveProductsBalance(ids, Сlient);

            // if (!result){
            //     throw new Exception("Temporary sync failure");
            // }
            return true;
        }
        catch (CircuitBrokenException ex)
        {
            Console.WriteLine($"API blocked: {ex.Message}");
            throw new JobPerformanceException("API is down", ex); 
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("SyncProductsAsync job was canceled");
            throw; 
        }
        catch (Exception ex) when (IsTransientError(ex))
        {
            Console.WriteLine("Transient error in SyncProductsAsync");
            throw; 
        }
        catch (Exception ex)
        {
            Console.WriteLine("Fatal error in SyncProductsAsync");
            return false;
        }
    }
    private bool IsTransientError(Exception ex)
    {
        return ex is TimeoutException 
               || ex is HttpRequestException
               || (ex.InnerException != null && IsTransientError(ex.InnerException));
    }
    private async Task<List<JsonElement>> FetchAndSaveProductsBalance(List<long> ids, HttpClient client)
    {
        const int MaxDegreeOfParallelism = 3; // 3 параллельных запроса
        const int DelayBetweenBatchesMs = 50; // Задержка между партиями
    
        var result = new ConcurrentBag<JsonElement>();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxDegreeOfParallelism
        };

        await Parallel.ForEachAsync(ids, options, async (id, ct) =>
        {
            try
            {
                // var response = await client.GetAsync($"/item/{id}", ct);
                // response.EnsureSuccessStatusCode();
                // var json = await response.Content.ReadAsStringAsync();
                // var root = JsonSerializer.Deserialize<JsonElement>(json);
                // var items = root.GetProperty("items");
                //
                // if (items.GetArrayLength() == 0) continue;
                // var product = items[0];
                // var productDict = product.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                // var content = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                // if (content != null)
                // {
                //     result.Add(content);
                // }
                //
                // // Небольшая задержка между запросами
                // await Task.Delay(DelayBetweenBatchesMs, ct);
            }
            catch (Exception ex)
            {
                // Логирование ошибки
                Console.WriteLine($"Error fetching product {id}: {ex.Message}");
            }
        });

        return result.ToList();
    }
}