using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WBSL.Data;
using WBSL.Models;

namespace WBSL.Controllers;

[ApiController]
[Route("api/[controller]")]
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

    [HttpGet("sync-categories")]
    public async Task<IActionResult> SyncCategories(){
        try{
            var parentCategories = await FetchParentCategories();
            var allCategories = new ConcurrentBag<wildberries_category>();

            var options = new ParallelOptions { MaxDegreeOfParallelism = 2 };
            var random = new Random();

            await Parallel.ForEachAsync(parentCategories, options, async (parent, ct) => 
            {
                try
                {
                    var subCategories = await FetchSubCategories(parent.id, parent.name, ct);
                    foreach (var category in subCategories)
                        allCategories.Add(category);
        
                    // Случайная задержка 0.5-1.5 сек между группами
                    await Task.Delay(TimeSpan.FromSeconds(0.5 + random.NextDouble()), ct);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error: " + ex.Message);
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

    public class WbApiResponse
    {
        public List<wildberries_parrent_category> data{ get; set; }
    }

    private async Task<List<wildberries_parrent_category>> FetchParentCategories(){
        var response = await WbClient.GetAsync("content/v2/object/parent/all");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<WbApiResponse>();
        return result?.data ?? new List<wildberries_parrent_category>();
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

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(), cancellationToken: ct);
    
        return doc.RootElement
            .GetProperty("data")
            .EnumerateArray()
            .Select(x => new wildberries_category
            {
                id = x.GetProperty("subjectID").GetInt32(),
                parent_id = x.GetProperty("parentID").GetInt32(),
                name = x.GetProperty("subjectName").GetString()!,
                parent_name = x.GetProperty("parentName").GetString()
            })
            .ToList();
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