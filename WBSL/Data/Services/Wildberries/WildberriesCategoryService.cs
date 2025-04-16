using System.Collections.Concurrent;
using System.Text.Json;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;
using WBSL.Data.HttpClientFactoryExt;
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
        PlatformHttpClientFactory httpFactory,
        QPlannerDbContext db) : base(httpFactory){
        _db = db;
    }

    public async Task<CategorySyncResult> SyncCategoriesAsync(){
        var accountId = 1;
        var cts = new CancellationTokenSource();
        
        var parentCategories = await FetchParentCategoriesAsync(accountId);
        
        var categories = await FetchAllCategoriesAsync(accountId, cts.Token);
        
        await SaveCategoriesToDatabaseAsync(parentCategories, categories);

        return new CategorySyncResult(
            ParentCategoriesCount: parentCategories.Count,
            SubCategoriesCount: categories.Count,
            ErrorsCount: 0 
        );
    }

    public async Task<List<WbCategoryDto>> GetParentCategoriesAsync(string query){
        var relatedCategories = await _db.wildberries_parrent_categories
            .Where(c => EF.Functions.ILike(c.name, $"%{query}%"))
            .Select(c => new WbCategoryDto
            {
                Id = c.id,
                Name = c.name
            })
            .ToListAsync();

        return relatedCategories;
    }
    
    public async Task<WbCategoryDto?> GetParentCategoryByIdAsync(int id){
        var relatedCategories = await _db.wildberries_parrent_categories
            .Where(c => c.id == id)
            .Select(c => new WbCategoryDto
            {
                Id = c.id,
                Name = c.name
            })
            .FirstOrDefaultAsync();

        return relatedCategories;
    }
    
    public async Task<List<WbCategoryDtoExt>> GetChildCategoriesAsync(string query){
        var relatedCategories = await _db.wildberries_categories
            .Where(c => EF.Functions.ILike(c.name, $"%{query}%"))
            .Select(c => new WbCategoryDtoExt
            {
                Id = c.id,
                Name = c.name,
                ParentId = c.parent_id
            })
            .ToListAsync();

        return relatedCategories;
    }

    private async Task<List<wildberries_category>> FetchAllCategoriesAsync(int accountId, CancellationToken ct){
        var wbClient = await GetWbClientAsync(accountId, true);
        var allCategories = new ConcurrentBag<wildberries_category>();
        var errorsCount = 0;
        int batchSize = 1000;
        int maxParallel = 3;

        var options = new ParallelOptions{
            MaxDegreeOfParallelism = maxParallel,
            CancellationToken = ct
        };

        var offsets = Enumerable.Range(0, 1000).Select(i => i * batchSize); // максимум 1 млн категорий — хватит
        var done = false;

        await Parallel.ForEachAsync(offsets, options, async (offset, token) => {
            if (done) return;

            try{
                var response =
                    await wbClient.GetAsync($"/content/v2/object/all?limit={batchSize}&offset={offset}", token);
                response.EnsureSuccessStatusCode();

                using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(),
                    cancellationToken: token);

                var data = doc.RootElement.GetProperty("data");
                if (data.GetArrayLength() == 0){
                    done = true;
                    return;
                }

                foreach (var x in data.EnumerateArray()){
                    allCategories.Add(new wildberries_category{
                        id = x.GetProperty("subjectID").GetInt32(),
                        parent_id = x.GetProperty("parentID").GetInt32(),
                        name = x.GetProperty("subjectName").GetString()!,
                        parent_name = x.GetProperty("parentName").GetString()
                    });
                }
            }
            catch (Exception ex){
                Console.WriteLine($"Ошибка при загрузке offset {offset}: {ex.Message}");
                Interlocked.Increment(ref errorsCount);
            }
        });

        return allCategories.ToList();
    }

    public class ParentCategoriesResponse
    {
        public List<wildberries_parrent_category> Data{ get; set; }
    }

    private async Task<List<wildberries_parrent_category>> FetchParentCategoriesAsync(int accountId){
        var WbClient = await GetWbClientAsync(accountId, true);
        var response = await WbClient.GetAsync("content/v2/object/parent/all");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ParentCategoriesResponse>();
        return result?.Data ?? new List<wildberries_parrent_category>();
    }

    private async Task<List<wildberries_category>> FetchSubCategoriesAsync(
        int parentId,
        int accountId,
        CancellationToken ct){
        var WbClient = await GetWbClientAsync(accountId, true);
        var response = await WbClient.GetAsync(
            $"content/v2/object/all?parentID={parentId}&limit=1000",
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