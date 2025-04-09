using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    public SimaLandController(IHttpClientFactory httpFactory, QPlannerDbContext db){
        _httpFactory = httpFactory;
        _db = db;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("store")]
    public async Task<IActionResult> StoreProductsAndAttributes([FromBody] StoreRequest request){
        foreach (var p in request.Products){
            if (p.width == 0) p.width = 1;
            if (p.height == 0) p.height = 1;
            if (p.depth == 0) p.depth = 1;
        }

        await _db.products.AddRangeAsync(request.Products);
        await _db.product_attributes.AddRangeAsync(request.Attributes);
        await _db.SaveChangesAsync();

        return Ok(new{ success = true, products = request.Products.Count, attributes = request.Attributes.Count });
    }


    [HttpPost("fetch")]
    public async Task<IActionResult> FetchProducts([FromBody] SimaRequest request){
        var userId = GetUserId();

        var account = await _db.external_accounts
            .FirstOrDefaultAsync(a => a.id == request.AccountId && a.user_id == userId && a.platform == "SimaLand");

        if (account == null)
            return BadRequest("Аккаунт не найден или не принадлежит вам.");

        var client = _httpFactory.CreateClient("SimaLand");
        client.DefaultRequestHeaders.Add("X-Api-Key", account.token);

        var results = new List<JsonElement>();
        var allAttributes = new List<product_attribute>();

        foreach (var sid in request.Articles){
            var response =
                await client.GetAsync(
                    $"item/?sid={sid}&expand=description,stocks,barcodes,attrs,category,trademark,country,unit,category_id");

            if (!response.IsSuccessStatusCode)
                continue;

            var json = await response.Content.ReadAsStringAsync();
            var root = JsonSerializer.Deserialize<JsonElement>(json);
            var items = root.GetProperty("items");

            if (items.GetArrayLength() == 0) continue;
            var product = items[0];
            var productDict = product.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

            if (productDict.TryGetValue("description", out var descElement)){
                var rawDesc = descElement.GetString() ?? "";
                var cleanDesc = Regex.Replace(rawDesc, "<.*?>", string.Empty);
                productDict["description"] = JsonSerializer.SerializeToElement(cleanDesc);
            }

            if (product.TryGetProperty("category_id", out var categoryIdElem)){
                var categoryId = categoryIdElem.GetInt32();
                var categoryResponse = await client.GetAsync($"category/{categoryId}/?expand=sub_categories");
                if (categoryResponse.IsSuccessStatusCode){
                    var categoryJson = await categoryResponse.Content.ReadAsStringAsync();
                    var categoryObj = JsonSerializer.Deserialize<JsonElement>(categoryJson);
                    if (categoryObj.TryGetProperty("name", out var nameElem))
                        productDict["category_name"] = nameElem;

                    if (categoryObj.TryGetProperty("sub_categories", out var subCatsElem)){
                        var subNames = subCatsElem.EnumerateArray()
                            .Where(c => c.TryGetProperty("name", out _))
                            .Select(c => c.GetProperty("name").GetString())
                            .ToList();
                        productDict["sub_category_names"] = JsonSerializer.SerializeToElement(subNames);
                    }
                }
            }

            if (product.TryGetProperty("trademark", out var trademarkProp) &&
                trademarkProp.ValueKind == JsonValueKind.Object &&
                trademarkProp.TryGetProperty("name", out var trademarkName))
                productDict["trademark_name"] = trademarkName;

            if (product.TryGetProperty("country", out var countryProp) &&
                countryProp.ValueKind == JsonValueKind.Object &&
                countryProp.TryGetProperty("name", out var countryName))
                productDict["country_name"] = countryName;

            if (product.TryGetProperty("unit", out var unitProp) && unitProp.ValueKind == JsonValueKind.Object &&
                unitProp.TryGetProperty("name", out var unitName))
                productDict["unit_name"] = unitName;

            if (productDict.TryGetValue("barcodes", out var barcodesElement)){
                var barcodes = barcodesElement.EnumerateArray().Select(b => b.GetString()).Where(s => s != null)
                    .ToList();
                productDict["barcodes"] = JsonSerializer.SerializeToElement(string.Join(",", barcodes));
            }

            if (product.TryGetProperty("id", out var itemIdElem) &&
                product.TryGetProperty("photoIndexes", out var indexesElem) &&
                product.TryGetProperty("photoVersions", out var versionsElem)){
                var itemId = itemIdElem.GetInt32();
                var versions = new Dictionary<string, int>();
                foreach (var versionElem in versionsElem.EnumerateArray()){
                    var number = versionElem.GetProperty("number").GetString() ?? "0";
                    var versionStr = versionElem.GetProperty("version").GetRawText();
                    if (int.TryParse(versionStr, out var version))
                        versions[number] = version;
                    else if (versionElem.TryGetProperty("version", out var verJson) &&
                             verJson.ValueKind == JsonValueKind.Number)
                        versions[number] = verJson.GetInt32();
                }

                var photoUrls = indexesElem.EnumerateArray()
                    .Select(indexElem => {
                        var index = indexElem.GetString() ?? "0";
                        var version = versions.TryGetValue(index, out var ver) ? ver : 0;
                        return $"https://goods-photos.static1-sima-land.com/items/{itemId}/{index}/700.jpg?v={version}";
                    })
                    .ToList();

                productDict["photo_urls"] = JsonSerializer.SerializeToElement(photoUrls);
            }

            if (product.TryGetProperty("attrs", out var attrsElem) && attrsElem.ValueKind == JsonValueKind.Array){
                foreach (var attr in attrsElem.EnumerateArray()){
                    var attrId = attr.GetProperty("attr_id").GetInt32();
                    var attrName = attr.TryGetProperty("attr_name", out var an) ? an.GetString() ?? "" : "";
                    var attrValue = attr.TryGetProperty("value", out var v) ? v.ToString() ?? "" : "";


                    allAttributes.Add(new product_attribute{
                        product_sid = sid,
                        attr_name = attrName,
                        value_text = attrValue
                    });
                }
            }

            var updatedJson = JsonSerializer.SerializeToElement(productDict);
            results.Add(updatedJson);
        }

        return Ok(new{
            products = results,
            attributes = allAttributes
        });
    }


    [HttpPost("export-excel")]
    public async Task<IActionResult> ExportExcel([FromBody] SimaRequest request){
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var account = await _db.external_accounts
            .FirstOrDefaultAsync(a => a.id == request.AccountId && a.user_id == userId && a.platform == "SimaLand");

        if (account == null)
            return BadRequest("Аккаунт не найден");

        var client = _httpFactory.CreateClient("SimaLand");
        client.DefaultRequestHeaders.Add("X-Api-Key", account.token);

        var workbook = new ClosedXML.Excel.XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Товары");

        var header = new[]{
            "Артикул", "Наименование", "Описание", "Ш×В×Г", "Вес", "Категория", "Остаток", "Мин. партия", "Опт. цена",
            "Розн. цена", "Фото"
        };

        for (int i = 0; i < header.Length; i++)
            worksheet.Cell(1, i + 1).Value = header[i];

        int row = 2;

        foreach (var article in request.Articles){
            var res = await client.GetAsync($"item/{article}?by_sid=true");
            if (!res.IsSuccessStatusCode) continue;

            var json = await res.Content.ReadAsStringAsync();
            var product = JsonSerializer.Deserialize<JsonElement>(json);

            var rawDesc = product.GetProperty("description").GetString() ?? "";
            var cleanDesc = Regex.Replace(rawDesc, "<.*?>", string.Empty);

            string categoryName = "";
            if (product.TryGetProperty("category_id", out var catIdElem)){
                var catRes = await client.GetAsync($"category/{catIdElem.GetInt32()}");
                if (catRes.IsSuccessStatusCode){
                    var catJson = await catRes.Content.ReadAsStringAsync();
                    var cat = JsonSerializer.Deserialize<JsonElement>(catJson);
                    categoryName = cat.GetProperty("name").GetString() ?? "";
                }
            }

            string photoUrls = "";
            if (product.TryGetProperty("base_photo_url", out var baseUrlElem) &&
                product.TryGetProperty("agg_photos", out var aggElem)){
                var baseUrl = baseUrlElem.GetString() ?? "";
                if (!baseUrl.EndsWith("/")) baseUrl += "/";

                var urls = aggElem.EnumerateArray()
                    .Select(p => $"{baseUrl}{p.GetInt32()}/700.jpg");

                photoUrls = string.Join(", ", urls);
            }

            worksheet.Cell(row, 1).Value = product.GetProperty("sid").GetInt64();
            worksheet.Cell(row, 2).Value = product.GetProperty("name").GetString();
            worksheet.Cell(row, 3).Value = cleanDesc;
            worksheet.Cell(row, 4).Value =
                $"{product.GetProperty("width")}×{product.GetProperty("height")}×{product.GetProperty("depth")}";
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

        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "simaland-products.xlsx");
    }


    public class SimaRequest
    {
        public int AccountId{ get; set; }
        public List<long> Articles{ get; set; } = new();
    }

    public class StoreRequest
    {
        public List<product> Products{ get; set; } = new();
        public List<product_attribute> Attributes{ get; set; } = new();
    }
}