using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using WBSL.Data.HttpClientFactoryExt;
using WBSL.Data.Services.Wildberries.Models;

namespace WBSL.Data.Services.Wildberries;

public class WildberriesCharacteristicsService : WildberriesBaseService
{
    private readonly QPlannerDbContext _db;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan _cacheDuration = TimeSpan.FromHours(1);
    
    private static readonly Dictionary<string, Func<WildberriesCharacteristicsService, int, Task<IEnumerable<string>>>> _valueGetters
        = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Цвет", (svc, accountId) => svc.GetColorNames(accountId) },
            { "Пол", (svc, accountId) => svc.GetKindNames(accountId) },
            { "Страна производства", (svc, accountId) => svc.GetCountryNames(accountId) }
        };
    public WildberriesCharacteristicsService(QPlannerDbContext db, PlatformHttpClientFactory factory, IMemoryCache cache) : base(factory)
    {
        _db = db;
        _cache = cache;
    }
    
    public async Task<IEnumerable<string>> GetCharacteristicValuesAsync(string name, int accountId)
    {
        if (!_valueGetters.TryGetValue(name, out var getter))
            return Enumerable.Empty<string>();

        var cacheKey = $"WbCharValues:{name}:{accountId}";
        if (_cache.TryGetValue(cacheKey, out IEnumerable<string> cachedValues))
            return cachedValues;

        var result = await getter(this, accountId);

        _cache.Set(cacheKey, result, _cacheDuration);
        return result;
    }
    
    private async Task<IEnumerable<string>> GetColorNames(int accountId)
    {
        var colors = await GetWbColorsApiAsync(accountId);
        return colors.Select(c => c.Name).Distinct().ToList();
    }

    private async Task<IEnumerable<string>> GetKindNames(int accountId)
    {
        var kinds = await GetWbKindsApiAsync(accountId);
        return kinds.Distinct().ToList();
    }

    private async Task<IEnumerable<string>> GetCountryNames(int accountId)
    {
        var countries = await GetWbCountriesApiAsync(accountId);
        return countries.Select(c => c.Name).Distinct().ToList();
    }
    
    private async Task<List<WbColor>> GetWbColorsApiAsync(int accountId){
        var WbClient = await GetWbClientAsync(accountId);
        var response = await WbClient.GetAsync("/content/v2/directory/colors");

        response.EnsureSuccessStatusCode();
        
        var responseData = await response.Content.ReadFromJsonAsync<JsonElement>();
        
        var colors = responseData
            .GetProperty("data")
            .EnumerateArray()
            .Select(color => new WbColor
            {
                Name = color.GetProperty("name").GetString(),
                ParentName = color.GetProperty("parentName").GetString()
            })
            .ToList();

        return colors;
    }

    private async Task<List<string>> GetWbKindsApiAsync(int accountId){
        var WbClient = await GetWbClientAsync(accountId);
        var response = await WbClient.GetAsync("/content/v2/directory/kinds");

        response.EnsureSuccessStatusCode();
        
        var responseData = await response.Content.ReadFromJsonAsync<JsonElement>();
        
        var kinds = responseData
            .GetProperty("data")
            .EnumerateArray()
            .Select(kind => kind.GetString()) 
            .ToList();

        return kinds;
    }

    private async Task<List<WbCountry>> GetWbCountriesApiAsync(int accountId){
        var WbClient = await GetWbClientAsync(accountId);
        var response = await WbClient.GetAsync("/content/v2/directory/countries");
        
        response.EnsureSuccessStatusCode();
        
        var responseData = await response.Content.ReadFromJsonAsync<JsonElement>();
        
        var countries = responseData
            .GetProperty("data")       // Получаем свойство "data"
            .EnumerateArray()          // Перебираем элементы массива
            .Select(country => new WbCountry
            {
                Name = country.GetProperty("name").GetString(),     // Извлекаем "name"
                FullName = country.GetProperty("fullName").GetString() // Извлекаем "fullName"
            })
            .ToList();                 // Преобразуем в список

        return countries;
    }
}