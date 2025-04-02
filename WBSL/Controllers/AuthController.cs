using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using WBSL.Data;        // Поменяй на свой namespace
using WBSL.Models;      // User модель
using Microsoft.AspNetCore.Authorization;

namespace WBSL.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly QPlannerDbContext _db;
    private readonly IConfiguration _config;
    private readonly IHttpContextAccessor _httpContextAccessor;
    

    public AuthController(QPlannerDbContext db, IConfiguration config,IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _config = config;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        if (await _db.users.AnyAsync(u => u.user_name == dto.UserName))
            return BadRequest("Пользователь с таким именем уже существует");

        var user = new user
        {
            id = Guid.NewGuid(),
            user_name = dto.UserName,
            email = dto.Email,
            password_hash = dto.Password // В боевом варианте — хэшируй!
        };

        _db.users.Add(user);
        await _db.SaveChangesAsync();

        var token = GenerateJwtToken(user);
        return Ok(new { token, user.user_name });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var user = await _db.users
            .FirstOrDefaultAsync(u => u.user_name == dto.UserName && u.password_hash == dto.Password);

        if (user == null)
            return Unauthorized("Неверный логин или пароль");

        var token = GenerateJwtToken(user);
        return Ok(new { token, user.user_name });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _db.users.FindAsync(Guid.Parse(userId));
        if (user == null) return NotFound();

        return Ok(new { user.id, user.user_name, user.email });
    }
    [HttpGet]
    public async Task<IActionResult> GetAccounts()
    {
        var userId = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null)
            return Unauthorized();

        var accounts = await _db.external_accounts
            .Where(a => a.user_id == Guid.Parse(userId))
            .ToListAsync();

        return Ok(accounts);
    }


    private string GenerateJwtToken(user user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_config["Jwt:Key"]);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.id.ToString()),
            new Claim(ClaimTypes.Name, user.user_name),
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddDays(7),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}



public class RegisterDto
{
    public string UserName { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string Password { get; set; } = default!;
}

public class LoginDto
{
    public string UserName { get; set; } = default!;
    public string Password { get; set; } = default!;
}
