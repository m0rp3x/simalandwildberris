using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WBSL.Data;
using WBSL.Models;

namespace WBSL.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AccountsController : ControllerBase
{
    private readonly QPlannerDbContext _db;

    public AccountsController(QPlannerDbContext db)
    {
        _db = db;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetAccounts()
    {
        var userId = GetUserId();
        var accounts = await _db.external_accounts
            .Where(a => a.user_id == userId)
            .Select(a => new
            {
                a.id,
                a.platform,
                a.name,
                a.added_at
            })
            .ToListAsync();

        return Ok(accounts);
    }

    [HttpPost]
    public async Task<IActionResult> AddAccount([FromBody] AddAccountDto dto)
    {
        var account = new external_account
        {
            user_id = GetUserId(),
            platform = dto.Platform,
            token = dto.Token,
            name = dto.Name,
            added_at = DateTime.Now
        };

        _db.external_accounts.Add(account);
        await _db.SaveChangesAsync();

        return Ok(account);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAccount(int id)
    {
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
    public string Platform { get; set; } = default!;
    public string Token { get; set; } = default!;
    public string Name { get; set; } = default!;
}
