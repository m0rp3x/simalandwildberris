using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WBSL.Models;
using Shared;

namespace WBSL.Data.Services.SimaLand;

public class SimaLandService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly QPlannerDbContext _db;

    public SimaLandService(IHttpClientFactory httpFactory, QPlannerDbContext db)
    {
        _httpFactory = httpFactory;
        _db = db;
    }

    public async Task<List<SimaProductDto>> GetProductsBasicAsync(List<long> sids, string apiKey)
    {
        var client = _httpFactory.CreateClient("SimaLand");
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var products = new List<SimaProductDto>();

        foreach (var sid in sids)
        {
            var response = await client.GetAsync($"item/?sid={sid}");
            if (!response.IsSuccessStatusCode) continue;

            var json = await response.Content.ReadAsStringAsync();
            var root = JsonSerializer.Deserialize<JsonElement>(json);

            if (!root.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
                continue;

            var item = items[0];
            var dto = new SimaProductDto
            {
                sid = item.GetProperty("sid").GetInt64(),
                name = item.GetProperty("name").GetString() ?? string.Empty,
                balance = item.TryGetProperty("balance", out var b) ? b.GetInt32() : 0
            };

            products.Add(dto);
        }

        return products;
    }
}