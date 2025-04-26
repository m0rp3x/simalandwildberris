using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using Shared.Enums;
using WBSL.Data;
using WBSL.Models;

namespace WBSL.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AccountsController : ControllerBase
{
    private readonly QPlannerDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;

    public AccountsController(QPlannerDbContext db, IHttpClientFactory httpClientFactory){
        _db = db;
        _httpClientFactory = httpClientFactory;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetAccounts(){
        var userId = GetUserId();
        var accounts = await _db.external_accounts
            .Where(a => a.user_id == userId)
            .Select(a => new{
                a.id,
                a.platform,
                a.name,
                a.added_at
            })
            .ToListAsync();

        return Ok(accounts);
    }

    [HttpPost]
    public async Task<IActionResult> AddAccount([FromBody] AddAccountDto dto){
        var account = new external_account{
            user_id = GetUserId(),
            platform = dto.Platform,
            token = dto.Token,
            name = dto.Name,
            added_at = DateTime.Now,
        };


        if (account.platform == "Wildberries"){
            account.warehouseid = await GetWbAccountWarehouseId(dto.Token);

            if (account.warehouseid == null)
                return BadRequest("Не удалось получить id склада.");
        }

        _db.external_accounts.Add(account);
        await _db.SaveChangesAsync();

        return Ok(account);
    }

    private async Task<int?> GetWbAccountWarehouseId(string token){
        var wbClient = _httpClientFactory.CreateClient(ExternalAccountType.WildBerriesMarketPlace.ToString());
        wbClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await wbClient.GetAsync("api/v3/warehouses");
        if (response.IsSuccessStatusCode)
        {
            var result = response.Content.ReadAsStringAsync().Result;

            var json = JsonSerializer.Deserialize<JsonElement>(result);
            if (json.ValueKind == JsonValueKind.Array && json.GetArrayLength() > 0)
            {
                var firstWarehouse = json[0];
                if (firstWarehouse.TryGetProperty("id", out var idProperty))
                {
                    return idProperty.GetInt32();
                }
            }
        }
        return null;
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAccount(int id){
        var userId = GetUserId();
        var account = await _db.external_accounts
            .FirstOrDefaultAsync(a => a.id == id && a.user_id == userId);

        if (account == null)
            return NotFound();

        _db.external_accounts.Remove(account);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}

public class AddAccountDto
{
    public string Platform{ get; set; } = default!;
    public string Token{ get; set; } = default!;
    public string Name{ get; set; } = default!;
}