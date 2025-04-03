using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    public async Task<IActionResult> SyncCategories(){
        try{
            var parentCategories = await FetchParentCategories();
            var allCategories = new ConcurrentBag<WildberriesCategories>();

            var options = new ParallelOptions{ MaxDegreeOfParallelism = 3 };

            await Parallel.ForEachAsync(parentCategories, options, async (parent, ct) => {
                try{
                    // Запросы автоматически ограничиваются хендлером
                    var subCategories = await FetchSubCategories(parent.Id, parent.Name, ct);
                    foreach (var category in subCategories)
                        allCategories.Add(category);
                }
                catch (Exception ex){
                    Console.Error.WriteLine($"Ошибка при обработке категории {parent.Name}: {ex.Message}");
                }
            });

            await SaveCategoriesToDatabase(parentCategories, allCategories.ToList());
            return Ok(new{
                ParentCategoriesCount = parentCategories.Count,
                SubCategoriesCount = allCategories.Count
            });
        }
        catch (Exception ex){
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

    private async Task<List<WildberriesCategories>> FetchSubCategories(
        int parentId,
        string parentName,
        CancellationToken ct){
        var response = await WbClient.GetAsync(
            $"content/v2/object/all?parentID={parentId}",
            ct
        );
        response.EnsureSuccessStatusCode();

        var subCategories = await response.Content.ReadFromJsonAsync<List<WildberriesCategories>>(cancellationToken: ct)
                            ?? new List<WildberriesCategories>();

        subCategories.ForEach(x => {
            x.ParentId = parentId;
            x.ParentName = parentName;
        });

        return subCategories;
    }

    private async Task SaveCategoriesToDatabase(
        List<WildberriesParrentCategories> parents,
        List<WildberriesCategories> categories){
        foreach (var parent in parents){
            await _db.WildberriesParrentCategories
                .Where(p => p.Id == parent.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.Name, parent.Name)
                );

            if (await _db.WildberriesParrentCategories.AllAsync(p => p.Id != parent.Id)){
                await _db.WildberriesParrentCategories.AddAsync(parent);
            }
        }

        foreach (var category in categories){
            await _db.WildberriesCategories
                .Where(c => c.Id == category.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.Name, category.Name)
                    .SetProperty(c => c.ParentId, category.ParentId)
                    .SetProperty(c => c.ParentName, category.ParentName)
                );

            if (await _db.WildberriesCategories.AllAsync(c => c.Id != category.Id)){
                await _db.WildberriesCategories.AddAsync(category);
            }
        }

        await _db.SaveChangesAsync();
    }
}

public class WildberriesRequest
{
}