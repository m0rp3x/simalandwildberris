using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace WBSL.Data.Services;

public class SettingsService
{
    private readonly ManualDbContextFactory _dbFactory;

    public SettingsService(IServiceScopeFactory scopeFactory)
    {
        _dbFactory = new ManualDbContextFactory(scopeFactory);
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