using System.Collections.Concurrent;
using System.Text.Json;
using EFCore.BulkExtensions;
using FuzzySharp;
using Microsoft.EntityFrameworkCore;
using Shared;
using WBSL.Data.HttpClientFactoryExt;
using WBSL.Models;

namespace WBSL.Data.Services.Wildberries;

public class WildberriesCategoryService : WildberriesBaseService
{
    private readonly string _simaSyncToken;
    private readonly string _wildberriesSyncToken;
    private readonly QPlannerDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;

    public WildberriesCategoryService(
        PlatformHttpClientFactory httpFactory,
        QPlannerDbContext db,
        IConfiguration config,
        IHttpClientFactory httpClientFactory) : base(httpFactory)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;

        _simaSyncToken = config.GetValue<string>("SimaLand:SyncToken")
                         ?? throw new InvalidOperationException("SimaLand SyncToken not configured");

        _wildberriesSyncToken = config.GetValue<string>("WildBerries:SyncToken")
                               ?? throw new InvalidOperationException("Wildberries SyncToken not configured");
    }

    private HttpClient CreateWildberriesClient()
    {
        var client = _httpClientFactory.CreateClient("Wildberries");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _wildberriesSyncToken);
        return client;
    }

    public record CategorySyncResult(int ParentCategoriesCount, int SubCategoriesCount, int ErrorsCount);

    public async Task<CategorySyncResult> SyncCategoriesAsync()
    {
        var cts = new CancellationTokenSource();

        var parentCategories = await FetchParentCategoriesAsync(cts.Token);
        var categories = await FetchAllCategoriesAsync(cts.Token);

        await SaveCategoriesToDatabaseAsync(parentCategories, categories);

        return new CategorySyncResult(
            ParentCategoriesCount: parentCategories.Count,
            SubCategoriesCount: categories.Count,
            ErrorsCount: 0
        );
    }

    public async Task<List<WbCategoryDto>> GetParentCategoriesAsync(string query)
    {
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

    public async Task<WbCategoryDto?> GetParentCategoryByIdAsync(int id)
    {
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

    public async Task<List<WbCategoryDtoExt>> GetChildCategoriesAsync(string query)
    {
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

    public async Task<string> SuggestCategoryAsync(int categoryId)
    {
        var wbCategory = await _db.wildberries_categories
            .AsNoTracking()
            .Where(c => c.id == categoryId)
            .Select(c => c.name)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(wbCategory))
            return "";

        var simaCategories = await _db.products
            .Select(p => p.category_name)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct()
            .ToListAsync();

        string? FindBestMatch(List<string> list, string target)
        {
            var result = list
                .Select(item => new {
                    Item = item,
                    Score = Fuzz.TokenSortRatio(item, target)
                }).MaxBy(x => x.Score);

            return result?.Score >= 70 ? result.Item : null;
        }

        var bestMatch = FindBestMatch(simaCategories, wbCategory);
        return bestMatch ?? "";
    }

    private async Task<List<wildberries_parrent_category>> FetchParentCategoriesAsync(CancellationToken ct)
    {
        var client = CreateWildberriesClient();
        var response = await client.GetAsync("content/v2/object/parent/all", ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ParentCategoriesResponse>(cancellationToken: ct);
        return result?.Data ?? new List<wildberries_parrent_category>();
    }

    private async Task<List<wildberries_category>> FetchAllCategoriesAsync(CancellationToken ct)
    {
        var client = CreateWildberriesClient();
        var allCategories = new ConcurrentBag<wildberries_category>();
        var errorsCount = 0;
        int batchSize = 1000;
        int maxParallel = 3;
        var done = false;

        var offsets = Enumerable.Range(0, 1000).Select(i => i * batchSize);

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxParallel,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(offsets, options, async (offset, token) =>
        {
            if (done) return;

            try
            {
                var response = await client.GetAsync($"/content/v2/object/all?limit={batchSize}&offset={offset}", token);
                response.EnsureSuccessStatusCode();

                using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(), cancellationToken: token);
                var data = doc.RootElement.GetProperty("data");

                if (data.GetArrayLength() == 0)
                {
                    done = true;
                    return;
                }

                foreach (var x in data.EnumerateArray())
                {
                    allCategories.Add(new wildberries_category
                    {
                        id = x.GetProperty("subjectID").GetInt32(),
                        parent_id = x.GetProperty("parentID").GetInt32(),
                        name = x.GetProperty("subjectName").GetString()!,
                        parent_name = x.GetProperty("parentName").GetString()
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при загрузке offset {offset}: {ex.Message}");
                Interlocked.Increment(ref errorsCount);
            }
        });

        return allCategories.ToList();
    }

    private async Task SaveCategoriesToDatabaseAsync(List<wildberries_parrent_category> parents, List<wildberries_category> categories)
    {
        await _db.BulkUpdateAsync(parents);

        var existingParentIds = await _db.wildberries_parrent_categories
            .Select(p => p.id)
            .ToListAsync();

        var newParents = parents
            .Where(p => !existingParentIds.Contains(p.id))
            .ToList();

        if (newParents.Any())
        {
            await _db.wildberries_parrent_categories.AddRangeAsync(newParents);
        }

        await _db.BulkUpdateAsync(categories);

        var existingCategoryIds = await _db.wildberries_categories
            .Select(c => c.id)
            .ToListAsync();

        var newCategories = categories
            .Where(c => !existingCategoryIds.Contains(c.id))
            .ToList();

        if (newCategories.Any())
        {
            await _db.wildberries_categories.AddRangeAsync(newCategories);
        }

        await _db.SaveChangesAsync();
    }

    public class ParentCategoriesResponse
    {
        public List<wildberries_parrent_category> Data { get; set; } = new();
    }
}
