using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WBSL.Data;
using WBSL.Data.Services;      // где лежит ProductShortenerService
using WBSL.Models;             // product_attribute
using Shared;                  // ShortenedDto
using static ProgressStore;

[ApiController]
[Route("api/gpt")]
public class GptController : ControllerBase
{
    private readonly QPlannerDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GptController> _logger;

    public GptController(
        QPlannerDbContext db,
        IServiceScopeFactory scopeFactory,
        ILogger<GptController> logger)
    {
        _db            = db;
        _scopeFactory  = scopeFactory;
        _logger        = logger;
    }

    public record PromptRequest(string Prompt, string Field, int MaxLength,bool GenerateIfEmpty = false);   
    public record StartJobResponse(Guid JobId);
    
    public record CountsDto(int NameCount, int DescriptionCount, int EmptyDescriptions);



    [HttpPost("start-shorten-job")]
    public IActionResult StartJob([FromBody] PromptRequest req)
    {
        if (req.Field != "name" && req.Field != "description")
            return BadRequest("Field must be 'name' or 'description'.");

        // 1) Извлекаем maxLen
        int maxLen = 60;
        var m = Regex.Match(req.Prompt, @"до\s*(\d+)\s*символ", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var pl)) 
            maxLen = pl;

        // 2) Считаем, сколько задач нужно
        int count = req.Field == "name"
            ? _db.products.Count(p => p.name.Length > maxLen)
            : _db.products.Count(p =>
                  // включаем пустые, если надо
                  (req.GenerateIfEmpty && string.IsNullOrWhiteSpace(p.description))
                  || (p.description != null && p.description.Length > maxLen)
              );

        _logger.LogInformation(
            "StartJob: Field={Field}, MaxLen={MaxLen}, GenerateEmpty={Gen}, ToProcess={Count}",
            req.Field, maxLen, req.GenerateIfEmpty, count
        );

        // 3) Создаём задачу
        var jobId = ProgressStore.CreateJob(count);

        // 4) Фоновый запуск
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<ProductShortenerService>();

            try
            {
                await svc.RunWithProgressAsync(
                    jobId,
                    req.Prompt,
                    req.Field,
                    maxLen,
                    req.GenerateIfEmpty
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background job {JobId} failed", jobId);
                var info = ProgressStore.GetJob(jobId);
                if (info != null) info.Status = ProgressStore.JobStatus.Failed;
            }
        });

        return Accepted(new StartJobResponse(jobId));
    }

    
    [HttpGet("shorten-progress/{jobId:guid}")]
    public IActionResult GetProgress(Guid jobId)
    {
        var info = ProgressStore.GetJob(jobId);
        if (info == null) return NotFound();
        return Ok(new {
            total     = info.Total,
            processed = info.Processed,
            status    = info.Status.ToString()
        });
    }

    [HttpGet("shorten-result/{jobId:guid}")]
    public IActionResult GetResult(Guid jobId)
    {
        var info = ProgressStore.GetJob(jobId);
        if (info == null) return NotFound();
        if (info.Status != JobStatus.Completed)
            return BadRequest("Job not completed");

        return Ok(new {
            products   = info.Products,
            attributes = info.Attributes
        });
    }
    [HttpGet("shorten-counts")]
    public ActionResult<CountsDto> GetCounts(
        [FromQuery] string prompt,
        [FromQuery] string field = "name",
        [FromQuery] bool generateIfEmpty = false)
    {
        int maxLen = 60;
        var match = Regex.Match(prompt, @"до\s*(\d+)\s*символ", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var parsed))
            maxLen = parsed;

        var nameCount = _db.products.Count(p => p.name.Length > maxLen);

        var descCount = _db.products.Count(p =>
            (!generateIfEmpty && p.description != null && p.description.Length > maxLen) ||
            (generateIfEmpty && string.IsNullOrWhiteSpace(p.description))
        );

        var emptyDescCount = _db.products.Count(p => string.IsNullOrWhiteSpace(p.description));

        return Ok(new CountsDto(nameCount, descCount, emptyDescCount));
    }





    
    [HttpPost("apply-shorten/{jobId:guid}")]
    public async Task<IActionResult> ApplyShortenAsync(Guid jobId)
    {
        var info = ProgressStore.GetJob(jobId);
        if (info == null) 
            return NotFound("Job not found");
        if (info.Status != JobStatus.Completed)
            return BadRequest("Job not completed");

        // Десериализуем накопленные DTO
        var dtos = info.Products
            .Select(el => JsonSerializer.Deserialize<ShortenedDto>(el.GetRawText())!)
            .ToList();

        // Применяем каждую правку
        foreach (var d in dtos)
        {
            var p = await _db.products.FindAsync(d.Sid);
            if (p == null) continue;
            if (d.Field == "name")        
                p.name = d.NewValue;
            else                        
                p.description = d.NewValue;
        }

        await _db.SaveChangesAsync();
        return Ok(new { Applied = dtos.Count });
    }


}
