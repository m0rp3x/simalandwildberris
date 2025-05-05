using Microsoft.AspNetCore.Mvc;
using WBSL.Data.Services;
using Shared;

namespace WBSL.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ExcelUpdateController : ControllerBase
{
    private readonly ExcelUpdateService _productService;

    public ExcelUpdateController(ExcelUpdateService productService)
    {
        _productService = productService;
    }
    [HttpPost("update-names")]
    public async Task<IActionResult> UpdateNames([FromBody] List<ProductNameUpdateDto> updates)
    {
        var updatedSids = await _productService.UpdateProductNamesAsync(updates);
        return Ok(new
        {
            updated = updatedSids.Count,
            updatedSids
        });
    }


    
    [HttpGet("export")]
    public async Task<IActionResult> ExportAllProducts()
    {
        var fileBytes = await _productService.ExportAllProductsToExcelAsync();
        return File(fileBytes, 
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 
            $"products_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
    }


}