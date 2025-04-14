using System.Collections.Concurrent;
using System.Net;
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

    public SimaLandController(IHttpClientFactory httpFactory, QPlannerDbContext db, IConfiguration config){
        _httpFactory = httpFactory;
        _db = db;
        _config = config;
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

        const int batchSize = 50;
        const int delayBetweenBatchesMs = 300;

        for (int i = 0; i < request.Articles.Count; i += batchSize){
            var batch = request.Articles.Skip(i).Take(batchSize).ToList();
            var batchTasks = batch.Select(async sid => {
                try{
                    var response = await client.GetAsync(
                        $"item/?sid={sid}&expand=description,stocks,barcodes,attrs,category,trademark,country,unit,category_id");
                    JsonElement product;

                    if (!response.IsSuccessStatusCode){
                        Console.WriteLine(
                            $"Ошибка при загрузке товара {sid}: {response.StatusCode}, пытаемся без expand...");
                        response = await client.GetAsync($"item/{sid}?by_sid=true");
                        if (!response.IsSuccessStatusCode){
                            Console.WriteLine(
                                $"Не удалось загрузить товар {sid} даже без expand: {response.StatusCode}");
                            return;
                        }

                        var fallbackJson = await response.Content.ReadAsStringAsync();
                        product = JsonDocument.Parse(fallbackJson).RootElement;
                    }
                    else{
                        var json = await response.Content.ReadAsStringAsync();
                        var root = JsonDocument.Parse(json);

                        if (root.RootElement.TryGetProperty("items", out var items) &&
                            items.ValueKind == JsonValueKind.Array &&
                            items.GetArrayLength() > 0){
                            product = items[0];
                        }
                        else if (root.RootElement.ValueKind == JsonValueKind.Object &&
                                 root.RootElement.TryGetProperty("sid", out _)){
                            product = root.RootElement;
                        }
                        else{
                            Console.WriteLine($"Пропущен товар {sid}: нет допустимого JSON объекта. Ответ: {json}");
                            return;
                        }
                    }

                    if (product.ValueKind != JsonValueKind.Object){
                        Console.WriteLine($"Пропущен товар {sid}: product не является объектом");
                        return;
                    }

                    var productDict = new Dictionary<string, JsonElement>();
                    foreach (var prop in product.EnumerateObject()){
                        try{
                            productDict[prop.Name] = prop.Value;
                        }
                        catch (Exception propEx){
                            Console.WriteLine(
                                $"Ошибка при обработке свойства {prop.Name} товара {sid}: {propEx.Message}");
                        }
                    }

                    if (productDict.TryGetValue("description", out var descElement)){
                        var rawDesc = descElement.GetString() ?? "";
                        var cleanDesc = Regex.Replace(rawDesc, "<.*?>", string.Empty);
                        productDict["description"] = JsonSerializer.SerializeToElement(cleanDesc);
                    }

                    if (productDict.TryGetValue("category_id", out var catIdElem) &&
                        catIdElem.ValueKind == JsonValueKind.Number){
                        var catId = catIdElem.GetInt32();
                        try{
                            var catResponse = await client.GetAsync($"category/{catId}/");
                            if (catResponse.IsSuccessStatusCode){
                                var catJson = await catResponse.Content.ReadAsStringAsync();
                                var catDoc = JsonDocument.Parse(catJson);
                                if (catDoc.RootElement.TryGetProperty("name", out var catName))
                                    productDict["category_name"] = catName;
                            }
                        }
                        catch (Exception catEx){
                            Console.WriteLine(
                                $"Ошибка при загрузке категории {catId} для товара {sid}: {catEx.Message}");
                        }
                    }

                    if (product.TryGetProperty("trademark", out var trademarkObj) &&
                        trademarkObj.ValueKind == JsonValueKind.Object &&
                        trademarkObj.TryGetProperty("name", out var trademarkName))
                        productDict["trademark_name"] = trademarkName;

                    if (product.TryGetProperty("country", out var countryObj) &&
                        countryObj.ValueKind == JsonValueKind.Object &&
                        countryObj.TryGetProperty("name", out var countryName))
                        productDict["country_name"] = countryName;

                    if (product.TryGetProperty("unit", out var unitObj) &&
                        unitObj.ValueKind == JsonValueKind.Object &&
                        unitObj.TryGetProperty("name", out var unitName))
                        productDict["unit_name"] = unitName;

                    if (productDict.TryGetValue("barcodes", out var barcodesElement) &&
                        barcodesElement.ValueKind == JsonValueKind.Array){
                        var barcodes = barcodesElement.EnumerateArray().Select(b => b.GetString())
                            .Where(s => s != null);
                        productDict["barcodes"] = JsonSerializer.SerializeToElement(string.Join(",", barcodes));
                    }

                    if (product.TryGetProperty("photoIndexes", out var indexesElem) &&
                        product.TryGetProperty("photoVersions", out var versionsElem)){
                        if (product.TryGetProperty("id", out var idElem) && idElem.ValueKind == JsonValueKind.Number){
                            var itemId = idElem.GetInt32();
                            var versions = new Dictionary<string, int>();
                            foreach (var x in versionsElem.EnumerateArray()){
                                var number = x.TryGetProperty("number", out var numProp)
                                    ? numProp.GetString() ?? "0"
                                    : "0";
                                if (x.TryGetProperty("version", out var verProp)){
                                    int parsedVersion = verProp.ValueKind switch{
                                        JsonValueKind.Number => verProp.GetInt32(),
                                        JsonValueKind.String when int.TryParse(verProp.GetString(), out var pv) => pv,
                                        _ => 0
                                    };
                                    versions[number] = parsedVersion;
                                }
                            }

                            var photoUrls = indexesElem.EnumerateArray()
                                .Select(indexElem => {
                                    var index = indexElem.GetString() ?? "0";
                                    var version = versions.TryGetValue(index, out var ver) ? ver : 0;
                                    return
                                        $"https://goods-photos.static1-sima-land.com/items/{itemId}/{index}/700.jpg?v={version}";
                                })
                                .ToList();

                            productDict["photo_urls"] = JsonSerializer.SerializeToElement(photoUrls);
                        }
                    }
                    else if (product.TryGetProperty("base_photo_url", out var baseUrlElem) &&
                             product.TryGetProperty("agg_photos", out var aggElem)){
                        var baseUrl = baseUrlElem.GetString() ?? "";
                        if (!baseUrl.EndsWith("/")) baseUrl += "/";
                        var urls = aggElem.EnumerateArray()
                            .Where(p => p.ValueKind == JsonValueKind.Number)
                            .Select(p => $"{baseUrl}{p.GetInt32()}/700.jpg")
                            .ToList();
                        productDict["photo_urls"] = JsonSerializer.SerializeToElement(urls);
                    }

                    if (product.TryGetProperty("attrs", out var attrsElem)){
                        foreach (var attr in attrsElem.EnumerateArray()){
                            var attrName = attr.TryGetProperty("attr_name", out var an) ? an.GetString() ?? "" : "";
                            var attrValue = attr.TryGetProperty("value", out var v) ? v.ToString() ?? "" : "";

                            if (!string.IsNullOrWhiteSpace(attrName)){
                                allAttributes.Add(new product_attribute{
                                    product_sid = sid,
                                    attr_name = attrName,
                                    value_text = attrValue
                                });
                            }
                        }
                    }

                    results.Add(JsonSerializer.SerializeToElement(productDict));
                }
                catch (Exception ex){
                    Console.WriteLine($"Ошибка при загрузке товара {sid}: {ex.Message}\n{ex.StackTrace}");
                }
            });

            await Task.WhenAll(batchTasks);
            await Task.Delay(delayBetweenBatchesMs);
        }

        return Ok(new{
            products = results,
            attributes = allAttributes
        });
    }


    [HttpPost("export-excel")]
    public async Task<IActionResult> ExportExcel([FromBody] List<Dictionary<string, object?>> productsData){
        var workbook = new ClosedXML.Excel.XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Товары");

        var headerCols = new List<string>{
            "Артикул", "Наименование", "Описание", "Ш×В×Г", "Упаковка", "Категория",
            "Опт. цена", "Розн. цена", "НДС", "Торговая марка", "Страна", "Фото"
        };

        var allAttrNames = new HashSet<string>();
        foreach (var p in productsData){
            foreach (var key in p.Keys){
                if (!headerCols.Contains(key))
                    allAttrNames.Add(key);
            }
        }

        headerCols.AddRange(allAttrNames);

        for (int i = 0; i < headerCols.Count; i++)
            worksheet.Cell(1, i + 1).Value = headerCols[i];

        int row = 2;
        foreach (var p in productsData){
            for (int col = 0; col < headerCols.Count; col++){
                var key = headerCols[col];
                worksheet.Cell(row, col + 1).Value =
                    p.TryGetValue(key, out var val) && val is not null ? val.ToString() : "";
            }

            row++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Seek(0, SeekOrigin.Begin);

        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "simaland-products.xlsx");
    }

    [HttpPost("download-photos")]
    public async Task<IActionResult> DownloadPhotos([FromBody] List<product> products){
        var savePath = _config.GetValue<string>("SimaLand:PhotoStoragePath");
        if (string.IsNullOrWhiteSpace(savePath))
            return BadRequest("Путь к папке хранения не задан в конфиге");

        const int maxConcurrentDownloads = 4; // снижено с 10 до 4
        const int maxErrors = 50;
        const int delayBetweenRequestsMs = 200; // добавлена задержка между запросами

        var errorCount = 0;
        var semaphore = new SemaphoreSlim(maxConcurrentDownloads);
        var downloadTasks = new List<Task>();
        var errorLock = new object();

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(20);

        foreach (var product in products){
            var folderPath = Path.Combine(savePath, product.sid.ToString());
            Directory.CreateDirectory(folderPath);

            int fallbackIndex = 0;

            foreach (var url in product.photo_urls ?? new List<string>()){
                await semaphore.WaitAsync();
                var localFallback = fallbackIndex++;

                downloadTasks.Add(Task.Run(async () => {
                    try{
                        var uri = new Uri(url);
                        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                        string indexPart = segments.Length >= 3 ? segments[^2] : localFallback.ToString();
                        var fileName = $"{product.sid}_{indexPart}.jpg";
                        var filePath = Path.Combine(folderPath, fileName);

                        var response = await httpClient.GetAsync(url);

                        if (!response.IsSuccessStatusCode){
                            lock (errorLock) errorCount++;
                            if (errorCount > maxErrors){
                                Console.WriteLine("🚫 Превышен лимит ошибок. Приостанавливаем загрузку.");
                                return;
                            }

                            Console.WriteLine($"⚠️ Не удалось загрузить {url}: {response.StatusCode}");
                            return;
                        }

                        var imageBytes = await response.Content.ReadAsByteArrayAsync();
                        await System.IO.File.WriteAllBytesAsync(filePath, imageBytes);
                    }
                    catch (Exception ex){
                        lock (errorLock) errorCount++;
                        Console.WriteLine($"❌ Ошибка при загрузке изображения: {url} — {ex.Message}");
                    }
                    finally{
                        await Task.Delay(delayBetweenRequestsMs); // задержка между запросами
                        semaphore.Release();
                    }
                }));
            }
        }

        await Task.WhenAll(downloadTasks);
        return Ok(new{ success = true });
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