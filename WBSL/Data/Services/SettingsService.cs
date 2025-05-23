using Microsoft.EntityFrameworkCore;

namespace WBSL.Data.Services;

public class SettingsService
{
    private readonly IDbContextFactory<QPlannerDbContext> _dbFactory;

    public SettingsService(IDbContextFactory<QPlannerDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<string?> GetAsync(string key)
    {
        await using var db = _dbFactory.CreateDbContext();
        return (await db.OrderSimSettings.FirstOrDefaultAsync(x => x.Key == key))?.Value;
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        var value = await GetAsync(key);
        return value != null ? (T?)Convert.ChangeType(value, typeof(T)) : default;
    }
}