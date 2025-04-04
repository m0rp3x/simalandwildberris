using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WBSL.Data;
using WBSL.Data.Services.Wildberries;

namespace WBSL.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WildberriesController : ControllerBase
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly QPlannerDbContext _db;
    private readonly WildberriesService _wildberriesService;
    private readonly WildberriesCategoryService _categoryService;
    private readonly WildberriesProductsService _productsService;
    private HttpClient WbClient => _httpFactory.CreateClient("WildBerries");

    public WildberriesController(QPlannerDbContext db, IHttpClientFactory clientFactory,
        WildberriesService wildberriesService, WildberriesProductsService productsService){
        _httpFactory = clientFactory;
        _db = db;
        _wildberriesService = wildberriesService;
        _productsService = productsService;
    }

    [HttpGet("wbItem/{vendorCode}")]
    public async Task<IActionResult> GetProduct(string vendorCode){
        var product = await _wildberriesService.GetProduct(vendorCode);
        return Ok(product);
    }

    [HttpGet("sync/categories")]
    public async Task<IActionResult> SyncCategories(){
        try{
            var result = await _categoryService.SyncCategoriesAsync();
            return Ok(result);
        }
        catch (Exception ex){
            Console.Error.WriteLine($"Критическая ошибка в SyncCategories: {ex.Message}");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("sync/productCards")]
    public async Task<IActionResult> SyncProductCards(){
        try{
            var result = await _productsService.SyncProductsAsync();
            return Ok(result);
        }
        catch (Exception ex){
            Console.Error.WriteLine($"Критическая ошибка в SyncCategories: {ex.Message}");
            return StatusCode(500, "Internal server error");
        }
    }
}

public class WildberriesRequest
{
}