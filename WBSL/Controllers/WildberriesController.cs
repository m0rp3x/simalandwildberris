using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using WBSL.Data;
using WBSL.Models;

namespace WBSL.Controllers;

public class WildberriesController : ControllerBase
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly QPlannerDbContext _db;
    private HttpClient WbClient => _httpFactory.CreateClient("WildBerries");

    public WildberriesController(QPlannerDbContext db, IHttpClientFactory clientFactory){
        _httpFactory = clientFactory;
        _db = db;
    }

    [HttpPost("fetch")]
    public async Task<IActionResult> FetchProducts([FromBody] WildberriesRequest request){
        return StatusCode(500);
    }

    [HttpPost("sync-categories")]
    public async Task<IActionResult> SyncCategories()
    {
        try
        {
            var parentCategories = await FetchParentCategories();

            var allCategories = new ConcurrentBag<WildberriesCategories>(); 

            var options = new ParallelOptions { MaxDegreeOfParallelism = 3 };
            var rateLimiter = new SemaphoreSlim(100, 100);

            using var minuteTimer = new System.Timers.Timer(60000)
            {
                AutoReset = true
            };
            minuteTimer.Elapsed += (_, _) => rateLimiter.Release(100);
            minuteTimer.Start();

            await Parallel.ForEachAsync(parentCategories, options, async (parent, ct) =>
            {
                try
                {
                    await rateLimiter.WaitAsync(ct).ConfigureAwait(false);
                    var subCategories = await FetchSubCategories(parent.Id, parent.Name).ConfigureAwait(false);

                    foreach (var category in subCategories)
                        allCategories.Add(category);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Ошибка при обработке категории {parent.Name}: {ex.Message}");
                }
            });

            minuteTimer.Stop();

            await SaveCategoriesToDatabase(parentCategories, allCategories.ToList()).ConfigureAwait(false);

            return Ok(new
            {
                ParentCategoriesCount = parentCategories.Count,
                SubCategoriesCount = allCategories.Count
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Критическая ошибка в SyncCategories: {ex.Message}");
            return StatusCode(500, "Internal server error");
        }
    }


    private async Task<List<WildberriesParrentCategories>> FetchParentCategories(){
        var response = await WbClient.GetAsync("content/v2/object/parent/all");
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<WildberriesParrentCategories>>()
               ?? new List<WildberriesParrentCategories>();
    }

    private async Task<List<WildberriesCategories>> FetchSubCategories(int parentId, string parentName){
        var response = await WbClient.GetAsync($"content/v2/object/all?parentID={parentId}");
        response.EnsureSuccessStatusCode();

        var subCategories = await response.Content.ReadFromJsonAsync<List<WildberriesCategories>>()
                            ?? new List<WildberriesCategories>();

        // Добавляем ParentName
        subCategories.ForEach(x => {
            x.ParentId = parentId;
            x.ParentName = parentName;
        });

        return subCategories;
    }

    private async Task SaveCategoriesToDatabase(
        List<WildberriesParrentCategories> parents,
        List<WildberriesCategories> categories){
        // Очищаем старые данные
        //TODO: Сделать очищение
        // Сохраняем новые
        await _db.WildberriesParrentCategories.AddRangeAsync(parents);

        foreach (var category in categories){
            _db.WildberriesCategories.Add(new WildberriesCategories{
                Id = category.Id,
                ParentId = category.ParentId,
                Name = category.Name,
                ParentName = category.ParentName
            });
        }

        await _db.SaveChangesAsync();
    }
}

public class WildberriesRequest
{
}