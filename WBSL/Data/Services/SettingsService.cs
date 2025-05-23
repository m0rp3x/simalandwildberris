using Microsoft.EntityFrameworkCore;

namespace WBSL.Data.Services;

public class SettingsService
{
    private readonly QPlannerDbContext _db;

    public SettingsService(QPlannerDbContext db)
    {
        _db = db;
    }

    public async Task<string?> GetAsync(string key)
    {
        return (await _db.OrderSimSettings.FirstOrDefaultAsync(x => x.Key == key))?.Value;
    }

    public async int GetAsync<T>(string key)
    {
        var value = await GetAsync(key);
        return value != null ? (T?)Convert.ChangeType(value, typeof(T)) : default;
    }
}
