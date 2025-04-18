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
using WBSL.Services;

namespace WBSL.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SimaLandController : ControllerBase
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly QPlannerDbContext _db;
    private readonly IConfiguration _config;
    private readonly ISimaLandService _simaLandService;

    public SimaLandController(IHttpClientFactory httpFactory, QPlannerDbContext db,IConfiguration config,ISimaLandService simaLandService)
    {
        _httpFactory = httpFactory;
        _db = db;
        _config = config;
        _simaLandService = simaLandService;
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

       var (products, attributes) = await _simaLandService.FetchProductsAsync(account.token, request.Articles);

       return Ok(new
       {
           products,
           attributes
       });
   }
   

   [HttpPost("fetch-job")]
   public async Task<IActionResult> StartJob([FromBody] SimaRequest req)
   {
       var userId = GetUserId();
       var account = await _db.external_accounts
           .FirstOrDefaultAsync(a => a.id == req.AccountId && a.user_id == userId && a.platform == "SimaLand");
       if (account == null)
           return BadRequest("Аккаунт не найден или не принадлежит вам.");

       var jobId = _simaLandService.StartFetchJob(account.token, req.Articles);
       return Accepted(new { jobId });
   }

   [HttpGet("fetch-progress/{jobId}")]
   public IActionResult GetProgress(Guid jobId)
   {
       var info = ProgressStore.GetJob(jobId);
       if (info == null) return NotFound();
       return Ok(new { total = info.Total, processed = info.Processed, status = info.Status.ToString() });
   }

   [HttpGet("fetch-result/{jobId}")]
   public IActionResult GetResult(Guid jobId)
   {
       var info = ProgressStore.GetJob(jobId);
       if (info == null)
           return NotFound();
       if (info.Status != ProgressStore.JobStatus.Completed)
           return BadRequest(new { message = "Job not completed yet" });

       var result = new FetchResultDto
       {
           Products = info.Products ?? new List<JsonElement>(),
           Attributes = info.Attributes ?? new List<product_attribute>()
       };
       return Ok(result);
   }


   [HttpPost("store")]
   public async Task<IActionResult> StoreProductsAndAttributes([FromBody] StoreRequest request)
   {
       var existingSids = await _db.products
           .Where(p => request.Products.Select(x => x.sid).Contains(p.sid))
           .Select(p => p.sid)
           .ToListAsync();

       // Оставляем только новые продукты
       var newProducts = request.Products
           .Where(p => !existingSids.Contains(p.sid))
           .ToList();

       // Фильтруем атрибуты только тех товаров, которых ещё нет
       var newAttributes = request.Attributes
           .Select(a => new product_attribute {
               product_sid = a.product_sid,
               attr_name   = a.attr_name,
               value_text  = a.value_text
           })
           .ToList();

       // Подчищаем габариты
       foreach (var p in newProducts)
       {
           if (p.width == 0) p.width = 1;
           if (p.height == 0) p.height = 1;
           if (p.depth == 0) p.depth = 1;
       }

       if (newProducts.Any())
           await _db.products.AddRangeAsync(newProducts);

       if (newAttributes.Any())
           await _db.product_attributes.AddRangeAsync(newAttributes);

       await _db.SaveChangesAsync();

       return Ok(new
       {
           success = true,
           added_products = newProducts.Count,
           skipped_products = existingSids.Count,
           added_attributes = newAttributes.Count
       });
   }




[HttpPost("export-excel")]
public async Task<IActionResult> ExportExcel([FromBody] List<Dictionary<string, object?>> productsData)
{
    var workbook = new ClosedXML.Excel.XLWorkbook();
    var worksheet = workbook.Worksheets.Add("Товары");

    var headerCols = new List<string>
    {
        "Артикул", "Наименование", "Описание", "Ш×В×Г", "Упаковка", "Категория",
        "Опт. цена", "Розн. цена", "НДС", "Торговая марка", "Страна", "Фото"
    };

    var allAttrNames = new HashSet<string>();
    foreach (var p in productsData)
    {
        foreach (var key in p.Keys)
        {
            if (!headerCols.Contains(key))
                allAttrNames.Add(key);
        }
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

    const int maxConcurrentDownloads = 4; // снижено с 10 до 4
    const int maxErrors = 50;
    const int delayBetweenRequestsMs = 200; // добавлена задержка между запросами

    var errorCount = 0;
    var semaphore = new SemaphoreSlim(maxConcurrentDownloads);
    var downloadTasks = new List<Task>();
    var errorLock = new object();

    using var httpClient = new HttpClient();
    httpClient.Timeout = TimeSpan.FromSeconds(20);

    foreach (var product in products)
    {
        var folderPath = Path.Combine(savePath, product.sid.ToString());
        Directory.CreateDirectory(folderPath);

        int fallbackIndex = 0;

        foreach (var url in product.photo_urls ?? new List<string>())
        {
            await semaphore.WaitAsync();
            var localFallback = fallbackIndex++;

            downloadTasks.Add(Task.Run(async () =>
            {
                try
                {
                    var uri = new Uri(url);
                    var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    string indexPart = segments.Length >= 3 ? segments[^2] : localFallback.ToString();
                    var fileName = $"{product.sid}_{indexPart}.jpg";
                    var filePath = Path.Combine(folderPath, fileName);

                    var response = await httpClient.GetAsync(url);

                    if (!response.IsSuccessStatusCode)
                    {
                        lock (errorLock) errorCount++;
                        if (errorCount > maxErrors)
                        {
                            Console.WriteLine("🚫 Превышен лимит ошибок. Приостанавливаем загрузку.");
                            return;
                        }
                        Console.WriteLine($"⚠️ Не удалось загрузить {url}: {response.StatusCode}");
                        return;
                    }

                    var imageBytes = await response.Content.ReadAsByteArrayAsync();
                    await System.IO.File.WriteAllBytesAsync(filePath, imageBytes);
                }
                catch (Exception ex)
                {
                    lock (errorLock) errorCount++;
                    Console.WriteLine($"❌ Ошибка при загрузке изображения: {url} — {ex.Message}");
                }
                finally
                {
                    await Task.Delay(delayBetweenRequestsMs); // задержка между запросами
                    semaphore.Release();
                }
            }));
        }
    }

    await Task.WhenAll(downloadTasks);
    return Ok(new { success = true });
}

public class FetchResultDto
{
    public List<JsonElement> Products { get; set; }
    public List<product_attribute> Attributes { get; set; }
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
