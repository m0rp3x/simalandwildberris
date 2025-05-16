using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared;
using WBSL.Client.Pages;
using WBSL.Data;
using WBSL.Data.Services.Simaland;
using WBSL.Data.Services.Wildberries;

namespace WBSL.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class WildberriesController : ControllerBase
{
    private static DateTime _lastCategoriesSyncTime = DateTime.MinValue;
    private static DateTime _lastProductsSyncTime = DateTime.MinValue;
    private static readonly object _categoriesLock = new object();
    private static readonly object _productsLock = new object();

    private readonly QPlannerDbContext _db;
    private readonly WildberriesMappingService _wildberriesMappingService;
    private readonly WildberriesService _wildberriesService;
    private readonly WildberriesCategoryService _categoryService;
    private readonly WildberriesProductsService _productsService;
    private readonly WildberriesCharacteristicsService _characteristicsService;
    private readonly WildberriesPriceService _wildberriesPriceService;
    private readonly SimalandFetchService _simalandFetchService;

    public WildberriesController(QPlannerDbContext db, IHttpClientFactory clientFactory,
        WildberriesService wildberriesService, WildberriesProductsService productsService,
        WildberriesCategoryService categoryService,
        WildberriesCharacteristicsService characteristicsService, SimalandFetchService simalandFetchService,
        WildberriesPriceService wildberriesPriceService){
        _db = db;
        _wildberriesPriceService = wildberriesPriceService;
        _wildberriesService = wildberriesService;
        _productsService = productsService;
        _categoryService = categoryService;
        _characteristicsService = characteristicsService;
        _simalandFetchService = simalandFetchService;
        _wildberriesMappingService = new WildberriesMappingService();
    }

    [HttpGet("wbItem/{vendorCode}/{accountId:int}/{wbAccountId:int}")]
    public async Task<IActionResult> GetProduct(string vendorCode, int accountId, int wbAccountId){
        List<long> vendorCodes = new List<long>();
        vendorCodes.Add(long.Parse(vendorCode));

        var product = await _wildberriesService.GetProduct(vendorCode, wbAccountId);
        var simalandProduct =
            await _simalandFetchService.FetchProductsWithMergedAttributesAsync(accountId, vendorCodes);
        return Ok(new WbItemApiResponse(){
            wbProduct = product,
            SimalandProducts = simalandProduct,
        });
    }

    [HttpGet($"suggest-simaland-category")]
    public async Task<IActionResult> SuggestCategory([FromQuery] int categoryId){
        var simaCategories = await _categoryService.SuggestCategoryAsync(categoryId);

        return Ok(simaCategories);
    }

    [HttpGet("char-values")]
    public async Task<IActionResult> GetCharacteristicValues([FromQuery] string name, [FromQuery] string? query,
        [FromQuery] int accountId){
        var allValues = await _characteristicsService.GetCharacteristicValuesAsync(name, accountId);

        if (string.IsNullOrWhiteSpace(query)){
            var resultAll = allValues
                .Distinct()
                .Take(50);
            return Ok(resultAll);
        }

        var filtered = allValues
            .Where(v => v.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .Take(50);

        return Ok(filtered);
    }

    [HttpPost("updateWbItem/{wbAccountId:int}")]
    public async Task<IActionResult> UpdateProduct([FromBody] List<WbProductCardDto> products, int wbAccountId){
        var result = await _wildberriesService.UpdateWbItemsAsync(products, wbAccountId);

        return Ok(result);
    }

    [HttpPost("updateWbPrices/{wbAccountId:int}")]
    public async Task<IActionResult> UpdateProduct(
        [FromBody] PriceCalculatorSettingsDto settingsDto,
        int wbAccountId){
        var result = await _wildberriesPriceService
            .PushPricesToWildberriesAsync(settingsDto, wbAccountId);

        return Ok(result);
    }
    

    [HttpGet("retry-photos-progress/{jobId}")]
    public IActionResult GetProgress([FromRoute] Guid jobId)
    {
        var job = ProgressStore.GetJob(jobId);
        if (job == null) return NotFound();
        return Ok(new SimaLandImport.ProgressDto {
            total     = job.Total,
            processed = job.Processed,
            status    = job.Status.ToString()
        });
    }
    
    [HttpGet("retry-photos-result/{jobId}")]
    public IActionResult GetResult([FromRoute] Guid jobId)
    {
        var job = ProgressStore.GetJob(jobId);
        if (job == null) return NotFound();
        if (job.Status != ProgressStore.JobStatus.Completed) return BadRequest("Job ещё не завершён");
        return Ok(job.Result!);
    }
    
    [HttpPost("retry-photos-job/{accountId}")]
    public async Task<IActionResult> StartRetryJob(
        [FromRoute] int accountId,
        [FromBody] List<string> vendorCodes)
    {
        try{
            var jobId = await _wildberriesService.StartRetrySendPhotosJob(vendorCodes, accountId);
            return Ok(new { jobId });
        }
        catch(Exception e){
            return BadRequest(e.Message);
        }
    }

    [HttpPost("createWbItem/{wbAccountId:int}")]
    public async Task<IActionResult> CreateProduct([FromBody] CategoryMappingRequest mappingRequest, int wbAccountId){
        var warehouseId = await _db.external_accounts
            .AsNoTracking()
            .Where(acc => acc.id == wbAccountId)
            .Select(acc => acc.warehouseid)
            .SingleOrDefaultAsync();
        
        if (warehouseId == null)
            return BadRequest($"Account {wbAccountId} has no WarehouseId");
        
        var accountIdsInWarehouse = await _db.external_accounts
            .Where(a => a.warehouseid == warehouseId)
            .Select(a => a.id)
            .ToListAsync();
        
        var simaSidsInCategory = await _db.products
            .AsNoTracking()
            .Where(x => x.category_name.ToLower() == mappingRequest.SimalandCategoryName.ToLower())
            .Select(x => x.sid.ToString())
            .ToListAsync();

        var existingWbSids = await _db.WbProductCards
            .AsNoTracking()
            .Where(wpc =>
                wpc.externalaccount_id.HasValue &&
                accountIdsInWarehouse.Contains(wpc.externalaccount_id.Value) &&
                simaSidsInCategory.Contains(wpc.VendorCode)
            )
            .Select(wpc => wpc.VendorCode)
            .Distinct()
            .ToListAsync();

        var filteredSids = simaSidsInCategory
            .Except(existingWbSids)
            .Take(10000)
            .Select(s => long.TryParse(s, out var id) ? id : (long?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        var simaProducts = await _db.products
            .AsNoTracking()
            .Include(x => x.product_attributes)
            .Where(x => filteredSids.Contains(x.sid))
            .ToListAsync();

        List<WbCreateVariantInternalDto> products =
            _wildberriesMappingService.BuildProductsFromMapping(mappingRequest, simaProducts);

        var result = await _wildberriesService.CreteWbItemsAsync(products, wbAccountId);

        return Ok(result);
    }

    [HttpGet("characteristics/{subjectId}/{accountId}")]
    public async Task<IActionResult> GetCharacteristics(int subjectId, int? accountId){
        if (accountId == null || subjectId <= 0)
            return BadRequest("Некорректный subjectId или accountId");

        var characteristics = await _wildberriesService.GetProductChars(subjectId, accountId.Value);
        if (characteristics == null)
            return NotFound("Характеристики не найдены");

        return Ok(characteristics);
    }


    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories([FromQuery] string? query, [FromQuery] int? baseSubjectId){
        if (baseSubjectId == null)
            return BadRequest("baseSubjectId обязателен");

        var results = await _wildberriesService.SearchCategoriesAsync(query, baseSubjectId.Value);
        return Ok(results);
    }

    [HttpGet("childCategories")]
    public async Task<IActionResult> GetChildCategories([FromQuery] string? query, [FromQuery] int? parentId){
        if (parentId == null)
            return BadRequest("parentId обязателен");

        var results = await _wildberriesService.SearchCategoriesByParentIdAsync(query, parentId.Value);
        return Ok(results);
    }

    [HttpGet("parentCategories")]
    public async Task<IActionResult> GetParentCategories([FromQuery] string? query){
        var results = await _wildberriesService.SearchParentCategoriesAsync(query);

        return Ok(results);
    }


    [HttpGet("sync/categories")]
    public async Task<IActionResult> SyncCategories(){
        lock (_categoriesLock){
            var timeSinceLastSync = DateTime.UtcNow - _lastCategoriesSyncTime;
            if (timeSinceLastSync < TimeSpan.FromMinutes(30)){
                return StatusCode(429, "Синхронизация категорий возможна только раз в 30 минут.");
            }

            _lastCategoriesSyncTime = DateTime.UtcNow;
        }

        try{
            var result = await _categoryService.SyncCategoriesAsync();
            return Ok(result);
        }
        catch (Exception ex){
            Console.Error.WriteLine($"Ошибка синхронизации категорий: {ex.Message}");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("sync/productCards")]
    public async Task<IActionResult> SyncProductCards(){
        lock (_productsLock){
            var timeSinceLastSync = DateTime.UtcNow - _lastProductsSyncTime;
            if (timeSinceLastSync < TimeSpan.FromHours(1)){
                return StatusCode(429, "Синхронизация продуктов возможна только раз в час.");
            }

            _lastProductsSyncTime = DateTime.UtcNow;
        }

        try{
            var result = await _productsService.SyncProductsAsync();
            return Ok(result);
        }
        catch (Exception ex){
            Console.Error.WriteLine($"Ошибка синхронизации продуктов: {ex.Message}");
            return StatusCode(500, "Internal server error");
        }
    }
}

public class WildberriesRequest
{
}