using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WBSL.Data;
using WBSL.Models;

namespace WBSL.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SimaLandController : ControllerBase
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly QPlannerDbContext _db;

    public SimaLandController(IHttpClientFactory httpFactory, QPlannerDbContext db)
    {
        _httpFactory = httpFactory;
        _db = db;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("fetch")]
    public async Task<IActionResult> FetchProducts([FromBody] SimaRequest request)
    {
        var userId = GetUserId();

        var account = await _db.external_accounts
            .FirstOrDefaultAsync(a => a.id == request.AccountId && a.user_id == userId && a.platform == "SimaLand");

        if (account == null)
            return BadRequest("Аккаунт не найден или не принадлежит вам.");

        var client = _httpFactory.CreateClient();
        client.BaseAddress = new Uri("https://www.sima-land.ru/api/v5/");
        client.DefaultRequestHeaders.Add("X-Api-Key", account.token);

        var results = new List<JsonElement>();

        foreach (var article in request.Articles)
        {
            var response = await client.GetAsync($"item/{article}?by_sid=true");

            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, $"Ошибка артикула {article}: {response.ReasonPhrase}");

            var json = await response.Content.ReadAsStringAsync();
            var product = JsonSerializer.Deserialize<JsonElement>(json);

            var productDict = product.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

            // Очистка HTML-тегов из описания
            if (productDict.TryGetValue("description", out var descElement))
            {
                var rawDesc = descElement.GetString() ?? "";
                var cleanDesc = Regex.Replace(rawDesc, "<.*?>", string.Empty);
                productDict["description"] = JsonSerializer.SerializeToElement(cleanDesc);
            }

            // Получение названия категории
            if (product.TryGetProperty("category_id", out var categoryIdElement))
            {
                var categoryId = categoryIdElement.GetInt32();
                var catResponse = await client.GetAsync($"category/{categoryId}");

                if (catResponse.IsSuccessStatusCode)
                {
                    var catJson = await catResponse.Content.ReadAsStringAsync();
                    var category = JsonSerializer.Deserialize<JsonElement>(catJson);
                    if (category.TryGetProperty("name", out var nameElement))
                    {
                        productDict["category_name"] = nameElement;
                    }
                }
            }
            
            // Получение штрихкодов
            if (productDict.TryGetValue("barcodes", out var barcodesElement))
            {
                var barcodes = barcodesElement.EnumerateArray().Select(b => b.GetString()).Where(s => s != null).ToList();
                productDict["barcodes"] = JsonSerializer.SerializeToElement(string.Join(",", barcodes));
            }


            // Получение всех URL фотографий
            var photoUrls = new List<string>();
            if (productDict.TryGetValue("base_photo_url", out var basePhotoElement) &&
                productDict.TryGetValue("agg_photos", out var photoArray))
            {
                var baseUrl = basePhotoElement.GetString() ?? "";
                if (!baseUrl.EndsWith("/")) baseUrl += "/";

                photoUrls = photoArray.EnumerateArray()
                    .Select(p => $"{baseUrl}{p.GetInt32()}/700.jpg")
                    .ToList();

                productDict["photo_urls"] = JsonSerializer.SerializeToElement(photoUrls);
            }

            var updatedJson = JsonSerializer.SerializeToElement(productDict);
            results.Add(updatedJson);
        }

        return Ok(results);
    }

    [HttpPost("export-excel")]
    public async Task<IActionResult> ExportExcel([FromBody] SimaRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var account = await _db.external_accounts
            .FirstOrDefaultAsync(a => a.id == request.AccountId && a.user_id == userId && a.platform == "SimaLand");

        if (account == null)
            return BadRequest("Аккаунт не найден");

        var client = _httpFactory.CreateClient();
        client.BaseAddress = new Uri("https://www.sima-land.ru/api/v5/");
        client.DefaultRequestHeaders.Add("X-Api-Key", account.token);

        var workbook = new ClosedXML.Excel.XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Товары");

        var header = new[] {
            "Артикул", "Наименование", "Описание", "Ш×В×Г", "Вес", "Категория", "Остаток", "Мин. партия", "Опт. цена", "Розн. цена", "Фото"
        };

        for (int i = 0; i < header.Length; i++)
            worksheet.Cell(1, i + 1).Value = header[i];

        int row = 2;

        foreach (var article in request.Articles)
        {
            var res = await client.GetAsync($"item/{article}?by_sid=true");
            if (!res.IsSuccessStatusCode) continue;

            var json = await res.Content.ReadAsStringAsync();
            var product = JsonSerializer.Deserialize<JsonElement>(json);

            var rawDesc = product.GetProperty("description").GetString() ?? "";
            var cleanDesc = Regex.Replace(rawDesc, "<.*?>", string.Empty);

            string categoryName = "";
            if (product.TryGetProperty("category_id", out var catIdElem))
            {
                var catRes = await client.GetAsync($"category/{catIdElem.GetInt32()}");
                if (catRes.IsSuccessStatusCode)
                {
                    var catJson = await catRes.Content.ReadAsStringAsync();
                    var cat = JsonSerializer.Deserialize<JsonElement>(catJson);
                    categoryName = cat.GetProperty("name").GetString() ?? "";
                }
            }

            string photoUrls = "";
            if (product.TryGetProperty("base_photo_url", out var baseUrlElem) &&
                product.TryGetProperty("agg_photos", out var aggElem))
            {
                var baseUrl = baseUrlElem.GetString() ?? "";
                if (!baseUrl.EndsWith("/")) baseUrl += "/";

                var urls = aggElem.EnumerateArray()
                    .Select(p => $"{baseUrl}{p.GetInt32()}/700.jpg");

                photoUrls = string.Join(", ", urls);
            }

            worksheet.Cell(row, 1).Value = product.GetProperty("sid").GetInt64();
            worksheet.Cell(row, 2).Value = product.GetProperty("name").GetString();
            worksheet.Cell(row, 3).Value = cleanDesc;
            worksheet.Cell(row, 4).Value = $"{product.GetProperty("width")}×{product.GetProperty("height")}×{product.GetProperty("depth")}";
            worksheet.Cell(row, 5).Value = product.GetProperty("weight").GetInt32();
            worksheet.Cell(row, 6).Value = categoryName;    
            worksheet.Cell(row, 7).Value = product.GetProperty("balance").GetString();
            worksheet.Cell(row, 8).Value = product.GetProperty("qty_multiplier").GetInt32();
            worksheet.Cell(row, 9).Value = product.GetProperty("wholesale_price").GetDecimal();
            worksheet.Cell(row, 10).Value = product.GetProperty("price").GetDecimal();
            worksheet.Cell(row, 11).Value = photoUrls;

            row++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Seek(0, SeekOrigin.Begin);

        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "simaland-products.xlsx");
    }
    
    [HttpPost("store")]
    public async Task<IActionResult> StoreProducts([FromBody] List<product> products)
    {
        foreach (var p in products)
        {
            if (p.width == 0) p.width = 1;
            if (p.height == 0) p.height = 1;
            if (p.depth == 0) p.depth = 1;
        }

        await _db.products.AddRangeAsync(products);
        await _db.SaveChangesAsync();
        return Ok(new { success = true, count = products.Count });
    }

    public class SimaRequest
    {
        public int AccountId { get; set; }
        public List<long> Articles { get; set; } = new();
    }
}
