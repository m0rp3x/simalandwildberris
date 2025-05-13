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
        bool generateIfEmpty){
        // 1) Построить IQueryable в зависимости от поля и флага
        IQueryable<product> query = _db.products;

        if (field == "name"){
            // Сокращаем только длинные названия
            query = query.Where(p => p.name.Length > maxLenValue);
        }
        else{
            // Работает по description
            if (generateIfEmpty){
                // Только пустые описания
                query = query.Where(p => string.IsNullOrWhiteSpace(p.description));
            }
            else{
                // Только ненулевые и длиннее maxLenValue
                query = query.Where(p => p.description != null
                                         && p.description.Length > maxLenValue);
            }
        }

        var items = await query.ToListAsync();
        var updated = new List<ShortenedDto>();
        int batchSize = field == "name" ? 50 : 25;

        // 2) Пробегаем по списку чанками
        foreach (var chunk in items.Chunk(batchSize)){
            // 2.1) Готовим вход для GPT
            var originals = chunk.Select(p => {
                if (field == "description" && generateIfEmpty
                                           && string.IsNullOrWhiteSpace(p.description)){
                    // хотим, чтобы нейронка придумала описание по названию
                    return $"{p.sid},Название: {p.name}";
                }

                // обычное сокращение
                return $"{p.sid},{(field == "name" ? p.name : p.description!)}";
            }).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"Сократи до {maxLenValue} символов.");
            if (!string.IsNullOrWhiteSpace(prompt))
                sb.AppendLine(prompt);
            sb.AppendLine("Верни строки в формате SID|НовыйТекст — по одной на строке:");
            sb.Append(string.Join(Environment.NewLine, originals));

            // 2.2) Отправляем в GPT и парсим ответ
            var parsed = new List<(long Sid, string Value)>();
            try{
                var resp = await _gpt.ShortenAsync(sb.ToString(), string.Empty);
                var lines = resp
                    .Split(new[]{ '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines){
                    // Теперь разбиваем по '|'
                    var parts = line.Split('|', 2);
                    if (parts.Length != 2)
                        continue;
                    if (long.TryParse(parts[0].Trim(), out var sid))
                        parsed.Add((sid, parts[1].Trim()));
                }
            }
            catch (Exception ex){
                _log.LogWarning(ex, "GPT batch failed for chunk size {Size}", chunk.Length);
            }

            // 2.3) Накатываем в память и обновляем прогресс
            foreach (var (sid, newVal) in parsed){
                var p = chunk.FirstOrDefault(x => x.sid == sid);
                if (p == null) continue;

                var orig = field == "name" ? p.name : (p.description ?? "");
                if (!string.IsNullOrWhiteSpace(newVal)
                    && newVal.Length <= maxLenValue
                    && newVal != orig){
                    updated.Add(new ShortenedDto{
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
        
        // 4) Серилизуем DTO и завершаем job
        var elems = updated
            .Select(d => JsonSerializer.SerializeToElement(d))
            .ToList();
        ProgressStore.CompleteJob(jobId, elems, new List<product_attribute>());
    }
}