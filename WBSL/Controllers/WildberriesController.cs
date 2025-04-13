using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared;
using WBSL.Data;
using WBSL.Data.Services.Simaland;
using WBSL.Data.Services.Wildberries;

namespace WBSL.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
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
        var simalandProduct = await _simalandFetchService.FetchProductsAsync(accountId, vendorCodes);
        return Ok(new WbItemApiResponse(){
            wbProduct = product,
            SimalandProducts = simalandProduct,
        });
    }

    [HttpPost("updateWbItem/{wbAccountId:int}")]
    public async Task<IActionResult> GetProduct([FromBody] List<WbProductCardDto> products, int wbAccountId){
        var result = await _wildberriesService.UpdateWbItemsAsync(products, wbAccountId);
        
        return Ok(result);
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