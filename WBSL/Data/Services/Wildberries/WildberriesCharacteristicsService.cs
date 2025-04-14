using System.Text.Json;
using WBSL.Data.HttpClientFactoryExt;
using WBSL.Data.Services.Wildberries.Models;

namespace WBSL.Data.Services.Wildberries;

public class WildberriesCharacteristicsService : WildberriesBaseService
{
    private readonly QPlannerDbContext _db;

    public WildberriesCharacteristicsService(QPlannerDbContext db, PlatformHttpClientFactory factory) : base(factory){
        _db = db;
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

    private async Task<List<string>> GetWbSexesApiAsync(int accountId){
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