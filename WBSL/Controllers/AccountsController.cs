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

        List<int> warehouses = new();
        if (account.platform == "Wildberries"){
            warehouses = await GetWbAccountWarehouseIds(dto.Token);
            if (warehouses.Count == 0)
                return BadRequest("Не удалось получить ни одного id склада.");
            // для совместимости с существующей логикой:
            account.warehouseid = warehouses[0];
        }

        _db.external_accounts.Add(account);
        await _db.SaveChangesAsync();

        if (account.platform == ExternalAccountType.Wildberries.ToString()){
            var links = warehouses
                .Select(wid => new ExternalAccountWarehouse{
                    ExternalAccountId = account.id,
                    WarehouseId = wid
                });
            _db.Set<ExternalAccountWarehouse>().AddRange(links);
            await _db.SaveChangesAsync();
        }

        return Ok(account);
    }

    private async Task<List<int>> GetWbAccountWarehouseIds(string token){
        var wbClient = _httpClientFactory.CreateClient(
            ExternalAccountType.WildBerriesMarketPlace.ToString());
        wbClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await wbClient.GetAsync("api/v3/warehouses");
        if (!response.IsSuccessStatusCode)
            return new List<int>();

        await using var stream = await response.Content.ReadAsStreamAsync();
        var json = await JsonSerializer.DeserializeAsync<JsonElement>(
            stream,
            new JsonSerializerOptions{ PropertyNameCaseInsensitive = true });

        if (json.ValueKind != JsonValueKind.Array)
            return new List<int>();

        var ids = new List<int>();
        foreach (var item in json.EnumerateArray()){
            if (item.TryGetProperty("id", out var idProp)
                && idProp.ValueKind == JsonValueKind.Number){
                ids.Add(idProp.GetInt32());
            }
        }

        return ids;
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