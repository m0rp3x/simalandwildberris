    using System.Collections.Concurrent;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Microsoft.EntityFrameworkCore;
    using Shared;
    using WBSL.Data;
    using WBSL.Data.Client;
    using WBSL.Models;

    public class ProductShortenerService
    {
        private readonly QPlannerDbContext _db;
        private readonly IChadGptClient _gpt;
        private readonly ILogger<ProductShortenerService> _log;

        public ProductShortenerService(
            QPlannerDbContext db,
            IChadGptClient gpt,
            ILogger<ProductShortenerService> log){
            _db = db;
            _gpt = gpt;
            _log = log;
        }

     public async Task RunWithProgressAsync(
        Guid jobId,
        string prompt,
        string field,
        int maxLenValue,
        bool generateIfEmpty)
    {
        IQueryable<product> query = _db.products;

        if (field == "name")
            query = query.Where(p => p.name.Length > maxLenValue);
        else
        {
            if (generateIfEmpty)
                query = query.Where(p => string.IsNullOrWhiteSpace(p.description));
            else
                query = query.Where(p => p.description != null && p.description.Length > maxLenValue);
        }

        var items = await query.ToListAsync();
        int batchSize = field == "name" ? 10 : 5;
        int maxConcurrency = 2;

        var updated = new ConcurrentBag<ShortenedDto>();
        var semaphore = new SemaphoreSlim(maxConcurrency);

        try
        {
            var tasks = items.Chunk(batchSize).Select(async chunk =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var originals = chunk.Select(p =>
                    {
                        if (field == "description" && generateIfEmpty && string.IsNullOrWhiteSpace(p.description))
                            return $"{p.sid},Название: {p.name}";
                        return $"{p.sid},{(field == "name" ? p.name : p.description!)}";
                    }).ToList();

                    var sb = new StringBuilder();
                    sb.AppendLine($"Сократи до {maxLenValue} символов.");
                    if (!string.IsNullOrWhiteSpace(prompt))
                        sb.AppendLine(prompt);
                    sb.AppendLine("Верни строки в формате SID|НовыйТекст — по одной на строке:");
                    sb.Append(string.Join(Environment.NewLine, originals));

                    if (sb.Length > 3000)
                    {
                        _log.LogWarning("Слишком длинный запрос GPT: {Len} символов, будет усечён.", sb.Length);
                        sb.Length = 3000;
                    }

                    var parsed = new List<(long Sid, string Value)>();
                    try
                    {
                        var resp = await _gpt.ShortenAsync(sb.ToString(), string.Empty);
                        var lines = resp?
                            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                        foreach (var line in lines)
                        {
                            var parts = line.Split('|', 2);
                            if (parts.Length == 2 && long.TryParse(parts[0].Trim(), out var sid))
                                parsed.Add((sid, parts[1].Trim()));
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Ошибка GPT в чанке на {Size} товаров. Пропускаем.", chunk.Count());
                        return;
                    }

                    foreach (var (sid, newVal) in parsed)
                    {
                        var p = chunk.FirstOrDefault(x => x.sid == sid);
                        if (p == null) continue;

                        var orig = field == "name" ? p.name : (p.description ?? "");
                        if (!string.IsNullOrWhiteSpace(newVal)
                            && newVal.Length <= maxLenValue
                            && newVal != orig)
                        {
                            updated.Add(new ShortenedDto
                            {
                                Sid = sid,
                                Field = field,
                                OldValue = orig,
                                NewValue = newVal
                            });

                            if (field == "name") p.name = newVal;
                            else p.description = newVal;
                        }

                        ProgressStore.UpdateProgress(jobId);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ошибка при параллельной обработке задачи {JobId}", jobId);
            ProgressStore.MarkFailed(jobId);
            return;
        }

        try
        {
            var elems = updated
                .Select(d => JsonSerializer.SerializeToElement(d))
                .ToList();

            ProgressStore.CompleteJob(jobId, elems, new List<product_attribute>());
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ошибка сериализации результатов задачи {JobId}", jobId);
            ProgressStore.MarkFailed(jobId);
        }
    }
    }