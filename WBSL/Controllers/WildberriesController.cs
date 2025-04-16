using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared;
using WBSL.Data;
using WBSL.Data.Services.Simaland;
using WBSL.Data.Services.Wildberries;

namespace WBSL.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WildberriesController : ControllerBase
{
    private static DateTime _lastCategoriesSyncTime = DateTime.MinValue;
    private static DateTime _lastProductsSyncTime = DateTime.MinValue;
    private static readonly object _categoriesLock = new object();
    private static readonly object _productsLock = new object();

    private readonly QPlannerDbContext _db;
    private readonly WildberriesService _wildberriesService;
    private readonly WildberriesCategoryService _categoryService;
    private readonly WildberriesProductsService _productsService;
    private readonly WildberriesCharacteristicsService _characteristicsService;
    private readonly SimalandFetchService _simalandFetchService;

    public WildberriesController(QPlannerDbContext db, IHttpClientFactory clientFactory,
        WildberriesService wildberriesService, WildberriesProductsService productsService,
        WildberriesCategoryService categoryService,
        WildberriesCharacteristicsService characteristicsService, SimalandFetchService simalandFetchService){
        _db = db;
        _wildberriesService = wildberriesService;
        _productsService = productsService;
        _categoryService = categoryService;
        _characteristicsService = characteristicsService;
        _simalandFetchService = simalandFetchService;
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

    [HttpGet("checkWbAndSimaland/{vendorCode}/{accountId:int}/{wbAccountId:int}")]
    public async Task<IActionResult> CheckWbAndProduct(string vendorCode, int accountId, int wbAccountId){
        List<long> vendorCodes = new List<long>();
        vendorCodes.Add(long.Parse(vendorCode));

        var wbTask = _wildberriesService.GetProductWithOutCharacteristics(vendorCode, wbAccountId);
        var simaTask = _simalandFetchService.FetchProductsWithMergedAttributesAsync(accountId, vendorCodes);
        
        await Task.WhenAll(wbTask, simaTask);
        
        var product = wbTask.Result;
        var simalandProduct = simaTask.Result.FirstOrDefault();
                
        T? FindBestMatch<T>(List<T> list, string target, Func<T, string> getName)
        {
            return list.FirstOrDefault(x => string.Equals(getName(x).Trim(), target.Trim(), StringComparison.OrdinalIgnoreCase))
                   ?? list.FirstOrDefault(x => getName(x).Contains(target, StringComparison.OrdinalIgnoreCase))
                   ?? list.FirstOrDefault();
        }

        var baseCategories = await _categoryService.GetParentCategoriesAsync(simalandProduct.category_name);
        
        var baseCategory = FindBestMatch(baseCategories, simalandProduct.category_name, x=>x.Name);
        WbCategoryDtoExt? childCategory = null;

        if (baseCategory == null)
        {
            var childCategories = await _categoryService.GetChildCategoriesAsync(simalandProduct.category_name);
            childCategory = FindBestMatch(childCategories, simalandProduct.category_name, x=>x.Name);
            
            if (childCategory.ParentId != null)
            {
                baseCategory = await _categoryService.GetParentCategoryByIdAsync(childCategory.ParentId);
            }
        }

        return Ok(new ProductCheckResponse()
        {
            IsNullFromWb = product == null,
            SimalandProduct = simalandProduct,
            BaseCategory = baseCategory,
            ChildCategory = childCategory
        });
    }

    [HttpPost("updateWbItem/{wbAccountId:int}")]
    public async Task<IActionResult> UpdateProduct([FromBody] List<WbProductCardDto> products, int wbAccountId){
        var result = await _wildberriesService.UpdateWbItemsAsync(products, wbAccountId);

        return Ok(result);
    }

    [HttpPost("createWbItem/{wbAccountId:int}")]
    public async Task<IActionResult> CreateProduct([FromBody] List<WbCreateVariantInternalDto> products, int wbAccountId){
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