using System.Collections.Concurrent;
using System.Text.Json;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using WBSL.Models;

namespace WBSL.Data.Services.Wildberries;

public class WildberriesCategoryService : WildberriesBaseService
{
    public record CategorySyncResult(
        int ParentCategoriesCount,
        int SubCategoriesCount,
        int ErrorsCount
    );

    private readonly QPlannerDbContext _db;

    public WildberriesCategoryService(
        IHttpClientFactory httpFactory,
        QPlannerDbContext db) : base(httpFactory){
        _db = db;
    }

    public async Task<CategorySyncResult> SyncCategoriesAsync(){
        var errorsCount = 0;
        var parentCategories = await FetchParentCategoriesAsync();
        var allCategories = new ConcurrentBag<wildberries_category>();

        var options = new ParallelOptions{ MaxDegreeOfParallelism = 2 };
        var random = new Random();

        await Parallel.ForEachAsync(parentCategories, options, async (parent, ct) => {
            try{
                var subCategories = await FetchSubCategoriesAsync(parent.id, ct);
                foreach (var category in subCategories)
                    allCategories.Add(category);

                // Случайная задержка 0.5-1.5 сек между группами
                await Task.Delay(TimeSpan.FromSeconds(0.5 + random.NextDouble()), ct);
            }
            catch (Exception ex){
                Console.WriteLine("CRITICAL ERROR: " + ex.Message);
                errorsCount++;
            }
        });

        await SaveCategoriesToDatabaseAsync(parentCategories, allCategories.ToList());

        return new CategorySyncResult(
            ParentCategoriesCount: parentCategories.Count,
            SubCategoriesCount: allCategories.Count,
            ErrorsCount: errorsCount
        );
    }

    public class ParentCategoriesResponse
    {
        public List<wildberries_parrent_category> Data{ get; set; }
    }

    private async Task<List<wildberries_parrent_category>> FetchParentCategoriesAsync(){
        var response = await WbClient.GetAsync("content/v2/object/parent/all");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ParentCategoriesResponse>();
        return result?.Data ?? new List<wildberries_parrent_category>();
    }

    private async Task<List<wildberries_category>> FetchSubCategoriesAsync(
        int parentId,
        CancellationToken ct){
        var response = await WbClient.GetAsync(
            $"content/v2/object/all?parentID={parentId}",
            ct
        );
        response.EnsureSuccessStatusCode();

        using var doc =
            await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(), cancellationToken: ct);

        return doc.RootElement
            .GetProperty("data")
            .EnumerateArray()
            .Select(x => new wildberries_category{
                id = x.GetProperty("subjectID").GetInt32(),
                parent_id = x.GetProperty("parentID").GetInt32(),
                name = x.GetProperty("subjectName").GetString()!,
                parent_name = x.GetProperty("parentName").GetString()
            })
            .ToList();
    }

    private async Task SaveCategoriesToDatabaseAsync(
        List<wildberries_parrent_category> parents,
        List<wildberries_category> categories){
        await _db.BulkUpdateAsync(parents);

        // 2. Пакетное добавление новых родителей
        var existingParentIds = await _db.wildberries_parrent_categories
            .Select(p => p.id)
            .ToListAsync();

        var newParents = parents
            .Where(p => !existingParentIds.Contains(p.id))
            .ToList();

        if (newParents.Any()){
            await _db.wildberries_parrent_categories.AddRangeAsync(newParents);
        }

        // 3. Пакетное обновление дочерних категорий
        await _db.BulkUpdateAsync(categories);

        // 4. Пакетное добавление новых категорий
        var existingCategoryIds = await _db.wildberries_categories
            .Select(c => c.id)
            .ToListAsync();

        var newCategories = categories
            .Where(c => !existingCategoryIds.Contains(c.id))
            .ToList();

        if (newCategories.Any()){
            await _db.wildberries_categories.AddRangeAsync(newCategories);
        }

        // 5. Один вызов SaveChanges для всех операций
        await _db.SaveChangesAsync();
    }
}