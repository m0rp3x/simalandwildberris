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
            var allCategories = new ConcurrentBag<wildberries_category>();

            var options = new ParallelOptions{ MaxDegreeOfParallelism = 3 };

            await Parallel.ForEachAsync(parentCategories, options, async (parent, ct) => {
                try{
                    // Запросы автоматически ограничиваются хендлером
                    var subCategories = await FetchSubCategories(parent.id, parent.name, ct);
                    foreach (var category in subCategories)
                        allCategories.Add(category);
                }
                catch (Exception ex){
                    Console.Error.WriteLine($"Ошибка при обработке категории {parent.name}: {ex.Message}");
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


    private async Task<List<wildberries_parrent_category>> FetchParentCategories(){
        var response = await WbClient.GetAsync("content/v2/object/parent/all");
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<wildberries_parrent_category>>()
               ?? new List<wildberries_parrent_category>();
    }

    private async Task<List<wildberries_category>> FetchSubCategories(
        int parentId,
        string parentName,
        CancellationToken ct){
        var response = await WbClient.GetAsync(
            $"content/v2/object/all?parentID={parentId}",
            ct
        );
        response.EnsureSuccessStatusCode();

        var subCategories = await response.Content.ReadFromJsonAsync<List<wildberries_category>>(cancellationToken: ct)
                            ?? new List<wildberries_category>();

        subCategories.ForEach(x => {
            x.parent_id = parentId;
            x.parent_name = parentName;
        });

        return subCategories;
    }

    private async Task SaveCategoriesToDatabase(
        List<wildberries_parrent_category> parents,
        List<wildberries_category> categories){
        foreach (var parent in parents){
            await _db.wildberries_parrent_categories
                .Where(p => p.id == parent.id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.name, parent.name)
                );

            if (await _db.wildberries_parrent_categories.AllAsync(p => p.id != parent.id)){
                await _db.wildberries_parrent_categories.AddAsync(parent);
            }
        }

        foreach (var category in categories){
            await _db.wildberries_categories
                .Where(c => c.id == category.id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.name, category.name)
                    .SetProperty(c => c.parent_id, category.parent_id)
                    .SetProperty(c => c.parent_name, category.parent_name)
                );

            if (await _db.wildberries_categories.AllAsync(c => c.id != category.id)){
                await _db.wildberries_categories.AddAsync(category);
            }
        }

        await _db.SaveChangesAsync();
    }
}

public class WildberriesRequest
{
}