// Controllers/StatsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WBSL.Data;

[ApiController]
[Route("api/[controller]")]
public class StatsController : ControllerBase
{
    private readonly QPlannerDbContext _db;
    public StatsController(QPlannerDbContext db) => _db = db;

    [HttpGet("counts")]
    public async Task<IActionResult> GetEntityCounts()
    {
        var products                       = await _db.products.CountAsync();
        var productAttributes              = await _db.product_attributes.CountAsync();
        var wbProductCards                 = await _db.WbProductCards.CountAsync();
        var wbProductCardCharacteristics   = await _db.WbProductCardCharacteristics.CountAsync();

        return Ok(new
        {
            products,
            productAttributes,
            wbProductCards,
            wbProductCardCharacteristics
        });
    }
}