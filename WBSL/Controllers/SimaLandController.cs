using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WBSL.Client.Pages;
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
    private readonly IConfiguration _config;

    public SimaLandController(IHttpClientFactory httpFactory, QPlannerDbContext db,IConfiguration config)
    {
        _httpFactory = httpFactory;
        _db = db;
        _config = config;
    }

   private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

[HttpPost("store")]
public async Task<IActionResult> StoreProductsAndAttributes([FromBody] StoreRequest request)
{
    foreach (var p in request.Products)
    {
        if (p.width == 0) p.width = 1;
        if (p.height == 0) p.height = 1;
        if (p.depth == 0) p.depth = 1;
    }

    await _db.products.AddRangeAsync(request.Products);
    await _db.product_attributes.AddRangeAsync(request.Attributes);
    await _db.SaveChangesAsync();

    return Ok(new { success = true, products = request.Products.Count, attributes = request.Attributes.Count });
}


[HttpPost("fetch")]
public async Task<IActionResult> FetchProducts([FromBody] SimaRequest request)
{
    var userId = GetUserId();

    var account = await _db.external_accounts
        .FirstOrDefaultAsync(a => a.id == request.AccountId && a.user_id == userId && a.platform == "SimaLand");

    if (account == null)
        return BadRequest("Аккаунт не найден или не принадлежит вам.");

    var client = _httpFactory.CreateClient("SimaLand");
    client.DefaultRequestHeaders.Add("X-Api-Key", account.token);

    var results = new List<JsonElement>();
    var allAttributes = new List<product_attribute>();

    foreach (var sid in request.Articles)
    {
        var response = await client.GetAsync($"item/?sid={sid}&expand=description,stocks,barcodes,attrs,category,trademark,country,unit,category_id");

        if (!response.IsSuccessStatusCode)
            continue;

        var json = await response.Content.ReadAsStringAsync();
        var root = JsonSerializer.Deserialize<JsonElement>(json);
        var items = root.GetProperty("items");

        if (items.GetArrayLength() == 0) continue;
        var product = items[0];
        var productDict = product.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

        if (productDict.TryGetValue("description", out var descElement))
        {
            var rawDesc = descElement.GetString() ?? "";
            var cleanDesc = Regex.Replace(rawDesc, "<.*?>", string.Empty);
            productDict["description"] = JsonSerializer.SerializeToElement(cleanDesc);
        }

        if (product.TryGetProperty("category_id", out var categoryIdElem))
        {
            var categoryId = categoryIdElem.GetInt32();
            var categoryResponse = await client.GetAsync($"category/{categoryId}/?expand=sub_categories");
            if (categoryResponse.IsSuccessStatusCode)
            {
                var categoryJson = await categoryResponse.Content.ReadAsStringAsync();
                var categoryObj = JsonSerializer.Deserialize<JsonElement>(categoryJson);
                if (categoryObj.TryGetProperty("name", out var nameElem))
                    productDict["category_name"] = nameElem;

                if (categoryObj.TryGetProperty("sub_categories", out var subCatsElem))
                {
                    var subNames = subCatsElem.EnumerateArray()
                        .Where(c => c.TryGetProperty("name", out _))
                        .Select(c => c.GetProperty("name").GetString())
                        .ToList();
                    productDict["sub_category_names"] = JsonSerializer.SerializeToElement(subNames);
                }
            }
        }

        if (product.TryGetProperty("trademark", out var trademarkProp) && trademarkProp.ValueKind == JsonValueKind.Object && trademarkProp.TryGetProperty("name", out var trademarkName))
            productDict["trademark_name"] = trademarkName;

        if (product.TryGetProperty("country", out var countryProp) && countryProp.ValueKind == JsonValueKind.Object && countryProp.TryGetProperty("name", out var countryName))
            productDict["country_name"] = countryName;

        if (product.TryGetProperty("unit", out var unitProp) && unitProp.ValueKind == JsonValueKind.Object && unitProp.TryGetProperty("name", out var unitName))
            productDict["unit_name"] = unitName;

        if (productDict.TryGetValue("barcodes", out var barcodesElement))
        {
            var barcodes = barcodesElement.EnumerateArray().Select(b => b.GetString()).Where(s => s != null).ToList();
            productDict["barcodes"] = JsonSerializer.SerializeToElement(string.Join(",", barcodes));
        }

        if (product.TryGetProperty("photoIndexes", out var indexesElem) &&
            product.TryGetProperty("photoVersions", out var versionsElem))
        {
            // Старая логика — по photoIndexes и photoVersions
            var itemId = product.GetProperty("id").GetInt32();
            var versions = new Dictionary<string, int>();

            foreach (var versionElem in versionsElem.EnumerateArray())
            {
                var number = versionElem.GetProperty("number").GetString() ?? "0";
                if (versionElem.TryGetProperty("version", out var verJson) && verJson.ValueKind == JsonValueKind.Number)
                    versions[number] = verJson.GetInt32();
            }

            var photoUrls = indexesElem.EnumerateArray()
                .Select(indexElem =>
                {
                    var index = indexElem.GetString() ?? "0";
                    var version = versions.TryGetValue(index, out var ver) ? ver : 0;
                    return $"https://goods-photos.static1-sima-land.com/items/{itemId}/{index}/700.jpg?v={version}";
                })
                .ToList();

            productDict["photo_urls"] = JsonSerializer.SerializeToElement(photoUrls);
        }
        else if (product.TryGetProperty("base_photo_url", out var baseUrlElem) &&
                 product.TryGetProperty("agg_photos", out var aggElem))
        {
            // Новая логика — по base_photo_url и agg_photos
            var baseUrl = baseUrlElem.GetString() ?? "";
            if (!baseUrl.EndsWith("/")) baseUrl += "/";

            var urls = aggElem.EnumerateArray()
                .Where(p => p.ValueKind == JsonValueKind.Number)
                .Select(p => $"{baseUrl}{p.GetInt32()}/700.jpg")
                .ToList();

            productDict["photo_urls"] = JsonSerializer.SerializeToElement(urls);
        }
        
        if (product.TryGetProperty("photo_urls", out var photoUrlsElem))
        {
            productDict["photo_urls"] = photoUrlsElem;
        }


        if (product.TryGetProperty("attrs", out var attrsElem) && attrsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var attr in attrsElem.EnumerateArray())
            {
                var attrId = attr.GetProperty("attr_id").GetInt32();
                var attrName = attr.TryGetProperty("attr_name", out var an) ? an.GetString() ?? "" : "";
                var attrValue = attr.TryGetProperty("value", out var v) ? v.ToString() ?? "" : "";


                allAttributes.Add(new product_attribute
                {
                    product_sid = sid,
                    attr_name = attrName,
                    value_text = attrValue
                });
            }
        }

        var updatedJson = JsonSerializer.SerializeToElement(productDict);
        results.Add(updatedJson);
    }

    return Ok(new
    {
        products = results,
        attributes = allAttributes
    });
}



[HttpPost("export-excel")]
public async Task<IActionResult> ExportExcel([FromBody] SimaRequest request)
{
    var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var account = await _db.external_accounts
        .FirstOrDefaultAsync(a => a.id == request.AccountId && a.user_id == userId && a.platform == "SimaLand");
    if (account == null)
        return BadRequest("Аккаунт не найден");

    var client = _httpFactory.CreateClient("SimaLand");
    client.DefaultRequestHeaders.Add("X-Api-Key", account.token);

    var workbook = new ClosedXML.Excel.XLWorkbook();
    var worksheet = workbook.Worksheets.Add("Товары");

    var headerCols = new List<string>
    {
        "Артикул", "Наименование", "Описание", "Ш×В×Г", "Упаковка", "Категория", "Опт. цена", "Розн. цена", "НДС", "Торговая марка", "Страна", "Фото"
    };

    var allAttrNames = new HashSet<string>();
    var productsData = new List<Dictionary<string, object?>>();

    foreach (var article in request.Articles)
    {
        var res = await client.GetAsync($"item/{article}?by_sid=true&expand=attrs,trademark,country,category,unit");
        if (!res.IsSuccessStatusCode) continue;

        var json = await res.Content.ReadAsStringAsync();
        var product = JsonSerializer.Deserialize<JsonElement>(json);

        var productRow = new Dictionary<string, object?>();

        productRow["Артикул"] = product.GetProperty("sid").GetInt64();
        productRow["Наименование"] = product.GetProperty("name").GetString();

        var rawDesc = product.TryGetProperty("description", out var descElem) ? descElem.GetString() ?? "" : "";
        productRow["Описание"] = Regex.Replace(rawDesc, "<.*?>", string.Empty);

        productRow["Ш×В×Г"] = $"{product.GetProperty("width")}×{product.GetProperty("height")}×{product.GetProperty("depth")}";
        productRow["Упаковка"] = $"{product.GetProperty("box_depth")}×{product.GetProperty("box_width")}×{product.GetProperty("box_height")}";
        productRow["Категория"] = product.TryGetProperty("category", out var cat) && cat.TryGetProperty("name", out var cn) ? cn.GetString() : "";
        productRow["Опт. цена"] = product.GetProperty("wholesale_price").GetDecimal();
        productRow["Розн. цена"] = product.GetProperty("price").GetDecimal();
        productRow["НДС"] = product.TryGetProperty("vat", out var vatVal) ? vatVal.GetInt32() : 0;
        productRow["Торговая марка"] = product.TryGetProperty("trademark", out var tr) && tr.TryGetProperty("name", out var tn) ? tn.GetString() : "";
        productRow["Страна"] = product.TryGetProperty("country", out var co) && co.TryGetProperty("name", out var con) ? con.GetString() : "";

        string photoUrls = "";
        if (product.TryGetProperty("photo_urls", out var pUrls) && pUrls.ValueKind == JsonValueKind.Array)
        {
            var urls = pUrls.EnumerateArray().Select(p => p.GetString()).Where(p => !string.IsNullOrWhiteSpace(p));
            photoUrls = string.Join(", ", urls);
        }
        productRow["Фото"] = photoUrls;

        if (product.TryGetProperty("attrs", out var attrsElem) && attrsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var attr in attrsElem.EnumerateArray())
            {
                var attrName = attr.TryGetProperty("attr_name", out var an) ? an.GetString() ?? "" : "";
                var value = attr.TryGetProperty("value", out var val) ? val.ToString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(attrName))
                {
                    productRow[attrName] = value;
                    allAttrNames.Add(attrName);
                }
            }
        }

        productsData.Add(productRow);
    }

    headerCols.AddRange(allAttrNames);

    for (int i = 0; i < headerCols.Count; i++)
        worksheet.Cell(1, i + 1).Value = headerCols[i];

    int row = 2;
    foreach (var p in productsData)
    {
        for (int col = 0; col < headerCols.Count; col++)
        {
            var key = headerCols[col];
            worksheet.Cell(row, col + 1).Value = p.TryGetValue(key, out var val) && val is not null ? val.ToString() : "";

        }
        row++;
    }

    using var stream = new MemoryStream();
    workbook.SaveAs(stream);
    stream.Seek(0, SeekOrigin.Begin);

    return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "simaland-products.xlsx");
}
[HttpPost("download-photos")]
public async Task<IActionResult> DownloadPhotos([FromBody] List<product> products)
{
    var savePath = _config.GetValue<string>("SimaLand:PhotoStoragePath");
    if (string.IsNullOrWhiteSpace(savePath))
        return BadRequest("Путь к папке хранения не задан в конфиге");

    foreach (var product in products)
    {
        var folderPath = Path.Combine(savePath, product.sid.ToString());
        Directory.CreateDirectory(folderPath);

        int fallbackIndex = 0;

        foreach (var url in product.photo_urls ?? new List<string>())
        {
            try
            {
                var uri = new Uri(url);
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                string indexPart = segments.Length >= 3 ? segments[^2] : fallbackIndex++.ToString();
                var fileName = $"{product.sid}_{indexPart}.jpg";
                var filePath = Path.Combine(folderPath, fileName);

                using var http = new HttpClient();
                var imageBytes = await http.GetByteArrayAsync(url);
                await System.IO.File.WriteAllBytesAsync(filePath, imageBytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка при загрузке изображения: {url} — {ex.Message}");
            }
        }
    }

    return Ok(new { success = true });
}

    public class SimaRequest
    {
        public int AccountId { get; set; }
        public List<long> Articles { get; set; } = new();
    }
    public class StoreRequest
    {
        public List<product> Products { get; set; } = new();
        public List<product_attribute> Attributes { get; set; } = new();
    }
}
