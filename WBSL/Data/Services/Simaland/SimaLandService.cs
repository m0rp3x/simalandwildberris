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
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;
using Shared;
using WBSL.Models;


namespace WBSL.Services
{
    public interface ISimaLandService
    {
        Task<(List<JsonElement> Products, List<product_attribute> Attributes)> FetchProductsAsync(string token, List<long> sids);
        Guid StartFetchJob(string token, List<long> sids);
        Task<List<CategoryDto>> GetCategoriesAsync(string token);

    }

    public class SimaLandService : ISimaLandService
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<SimaLandService> _logger;
        private readonly TokenBucketRateLimiter _rateLimiter;
        private const int MaxErrorsPerPeriod = 50;
        private const int MaxParallelRequests = 50;

        public SimaLandService(IHttpClientFactory httpFactory, ILogger<SimaLandService> logger)
        {
            _httpFactory = httpFactory;
            _logger = logger;
            _rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = 250,
                QueueLimit = 0,
                ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                TokensPerPeriod = 250,
                AutoReplenishment = true
            });
        }
        
        

        public async Task<(List<JsonElement> Products, List<product_attribute> Attributes)> FetchProductsAsync(string token, List<long> sids)
        {
            var productBag = new ConcurrentBag<JsonElement>();
            var attrBag = new ConcurrentBag<product_attribute>();
            var jobId = ProgressStore.CreateJob(sids.Count);
            var errorCount = 0;
            using var cts = new CancellationTokenSource();

            var client = _httpFactory.CreateClient("SimaLand");
            client.DefaultRequestHeaders.Add("X-Api-Key", token);

            var semaphore = new SemaphoreSlim(MaxParallelRequests);
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxParallelRequests,
                CancellationToken = cts.Token
            };

            await Parallel.ForEachAsync(sids, parallelOptions, async (sid, cancellationToken) =>
            {
                if (Interlocked.CompareExchange(ref errorCount, 0, 0) >= MaxErrorsPerPeriod)
                {
                    cts.Cancel();
                    return;
                }

                await semaphore.WaitAsync(cancellationToken);
                using var lease = await _rateLimiter.AcquireAsync(1, cancellationToken);
                if (!lease.IsAcquired)
                {
                    _logger.LogWarning("Rate limit exceeded for SID {Sid}", sid);
                    semaphore.Release();
                    return;
                }

                try
                {
                    var dict = await FetchProductAsync(client, sid, cancellationToken);
                    if (dict != null)
                    {
                        ExtractAttributes(dict, sid, attrBag);
                        dict.Remove("attrs");
                        ProgressStore.UpdateProgress(jobId);
                        productBag.Add(JsonSerializer.SerializeToElement(dict));
                    }
                }
                catch (Exception ex)
                {
                    var count = Interlocked.Increment(ref errorCount);
                    _logger.LogError(ex, "Error processing SID {Sid}, error #{Count}", sid, count);
                    if (count >= MaxErrorsPerPeriod)
                    {
                        _logger.LogError("Exceeded max error threshold ({MaxErrorsPerPeriod}), aborting.", MaxErrorsPerPeriod);
                        cts.Cancel();
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });
            ProgressStore.CompleteJob(jobId, productBag.ToList(), attrBag.ToList());

            return (productBag.ToList(), attrBag.ToList());
        }
        
           public Guid StartFetchJob(string token, List<long> sids)
    {
        // Создаем запись в хранилище прогресса
        var jobId = ProgressStore.CreateJob(sids.Count);

        // Запускаем фоновую задачу
        _ = Task.Run(async () =>
        {
            try
            {
                var (products, attributes) = await RunFetchInternalAsync(token, sids, jobId);
                // По завершении отмечаем job как завершенный
                ProgressStore.CompleteJob(jobId, products, attributes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background job {JobId} failed", jobId);
                if (ProgressStore.GetJob(jobId) is var info && info != null)
                    info.Status = ProgressStore.JobStatus.Failed;
            }
        });

        return jobId;
    }
           
           public async Task<List<CategoryDto>> GetCategoriesAsync(string token)
    {
        var client = _httpFactory.CreateClient("SimaLand");
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("X-Api-Key", token);

        // 1) Получаем все категории вместе с sub_categories и active_sub_categories
        var listResp = await client.GetAsync("category/?expand=sub_categories,active_sub_categories,name_alias");
        listResp.EnsureSuccessStatusCode();
        using var listDoc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync());
        var root = listDoc.RootElement;

        // если сервер вернул { items: [...] } — работаем с ним, иначе с корневым массивом
        var array = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var items) 
            ? items 
            : root;

        var categories = new List<CategoryDto>();
        foreach (var elem in array.EnumerateArray())
        {
            categories.Add(ParseCategory(elem));
        }

        // 2) Параллельно дозапрашиваем name_alias для каждой категории (одноуровневая «плоская» коллекция)
        var flat = new List<CategoryDto>();
        void Flatten(CategoryDto c)
        {
            flat.Add(c);
            foreach (var sub in c.SubCategories) Flatten(sub);
            foreach (var asc in c.ActiveSubCategories) Flatten(asc);
        }
        foreach (var cat in categories) Flatten(cat);

        
        return categories;
    }

    private CategoryDto ParseCategory(JsonElement e)
    {
        // Базовые поля
        var dto = new CategoryDto
        {
            Id = e.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number
                ? idProp.GetInt32() : 0,
            Name = e.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                ? nameProp.GetString()! : "",
            // здесь сразу попытаемся взять alias, но скорее всего он пуст — он подтянется этапом выше
            NameAlias = e.TryGetProperty("full_slug", out var aliasProp) && aliasProp.ValueKind == JsonValueKind.String
                ? aliasProp.GetString()! : "",

            ItemsCount = e.TryGetProperty("items_count", out var ic) && ic.ValueKind == JsonValueKind.Number
                ? ic.GetInt32() : 0,
            ItemsParentCount = e.TryGetProperty("items_parent_count", out var ipc) && ipc.ValueKind == JsonValueKind.Number
                ? ipc.GetInt32() : 0,
        };

        // Подкатегории
        if (e.TryGetProperty("sub_categories", out var sc) && sc.ValueKind == JsonValueKind.Array)
            dto.SubCategories = sc.EnumerateArray().Select(ParseCategory).ToList();

        if (e.TryGetProperty("active_sub_categories", out var asc) && asc.ValueKind == JsonValueKind.Array)
            dto.ActiveSubCategories = asc.EnumerateArray().Select(ParseCategory).ToList();

        return dto;
    }

    



    /// <summary>
    /// Общая логика загрузки: если jobId != null — обновляет ProgressStore.
    /// </summary>
    private async Task<(List<JsonElement> products, List<product_attribute> attrs)> RunFetchInternalAsync(
        string token, List<long> sids, Guid? jobId)
    {
        var productBag = new ConcurrentBag<JsonElement>();
        var attrBag    = new ConcurrentBag<product_attribute>();
        var errorCount = 0;
        using var cts = new CancellationTokenSource();

        var client = _httpFactory.CreateClient("SimaLand");
        client.DefaultRequestHeaders.Add("X-Api-Key", token);

        var semaphore = new SemaphoreSlim(MaxParallelRequests);
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxParallelRequests,
            CancellationToken = cts.Token
        };

        await Parallel.ForEachAsync(sids, parallelOptions, async (sid, cancellationToken) =>
        {
            if (Interlocked.CompareExchange(ref errorCount, 0, 0) >= MaxErrorsPerPeriod)
            {
                cts.Cancel();
                return;
            }

            await semaphore.WaitAsync(cancellationToken);
            using var lease = await _rateLimiter.AcquireAsync(1, cancellationToken);
            if (!lease.IsAcquired)
            {
                _logger.LogWarning("Rate limit exceeded for SID {Sid}", sid);
                semaphore.Release();
                return;
            }

            try
            {
                var dict = await FetchProductAsync(client, sid, cancellationToken);
                if (dict != null)
                {
                    ExtractAttributes(dict, sid, attrBag);
                    dict.Remove("attrs");
                    productBag.Add(JsonSerializer.SerializeToElement(dict));
                }
            }
            catch (Exception ex)
            {
                var count = Interlocked.Increment(ref errorCount);
                _logger.LogError(ex, "Error processing SID {Sid}, error #{Count}", sid, count);
                if (count >= MaxErrorsPerPeriod)
                {
                    _logger.LogError("Exceeded max error threshold ({MaxErrorsPerPeriod}), aborting.", MaxErrorsPerPeriod);
                    cts.Cancel();
                }
            }
            finally
            {
                // Обновляем прогресс, если это фоновый job
                if (jobId.HasValue)
                    ProgressStore.UpdateProgress(jobId.Value);

                semaphore.Release();
            }
        });

        return (productBag.ToList(), attrBag.ToList());
    }
        
        

        private async Task<Dictionary<string, JsonElement>?> FetchProductAsync(HttpClient client, long sid, CancellationToken cancellationToken)
        {
            var resp = await client.GetAsync($"item/?sid={sid}&expand=description,stocks,barcodes,attrs,category,trademark,country,unit,category_id,min_qty", cancellationToken);
            JsonElement product;

            if (resp.IsSuccessStatusCode)
            {
                var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancellationToken));
                if (!TryExtract(doc, out product)) return null;
            }
            else
            {
                _logger.LogWarning("Primary fetch failed for SID {Sid}: {Status}", sid, resp.StatusCode);
                resp = await client.GetAsync($"item/{sid}?by_sid=true", cancellationToken);
                if (!resp.IsSuccessStatusCode) return null;
                product = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancellationToken)).RootElement;
            }
            
            // извлекаем все базовые поля
            var dict = ExtractFields(product);

            // обогащаем и вытаскиваем всё остальное
            await EnrichCategory(client, dict, sid, cancellationToken);
            ExtractTrademarkCountryUnit(product, dict);
            ExtractBarcodes(dict);
            ExtractPhotos(product, dict);

            
            if (product.TryGetProperty("min_qty", out var minQtyElem))
            {
                dict["qty_multiplier"] = minQtyElem;
            }
            else
            {
                dict.Remove("qty_multiplier");
            }
            // --- КОНЕЦ НОВОГО БЛОКА ---

            if (product.TryGetProperty("attrs", out var arr) && arr.ValueKind == JsonValueKind.Array)
                dict["attrs"] = arr;
            return dict;
        }

        private bool TryExtract(JsonDocument doc, out JsonElement elem)
        {
            if (doc.RootElement.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
            {
                elem = arr[0]; return true;
            }
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("sid", out _))
            {
                elem = doc.RootElement; return true;
            }
            elem = default;
            return false;
        }

        private Dictionary<string, JsonElement> ExtractFields(JsonElement e)
        {
            var d = new Dictionary<string, JsonElement>();
            foreach (var p in e.EnumerateObject()) d[p.Name] = p.Value;
            if (d.TryGetValue("description", out var desc))
            {
                var s = Regex.Replace(desc.GetString() ?? string.Empty, "<.*?>", string.Empty);
                d["description"] = JsonSerializer.SerializeToElement(s);
            }
            return d;
        }

        private async Task EnrichCategory(HttpClient client, Dictionary<string, JsonElement> d, long sid, CancellationToken cancellationToken)
        {
            if (!d.TryGetValue("category_id", out var cid) || cid.ValueKind != JsonValueKind.Number) return;
            var id = cid.GetInt32();
            try
            {
                var r = await client.GetAsync($"category/{id}/?expand=sub_categories,active_sub_categories", cancellationToken);
                if (!r.IsSuccessStatusCode) return;
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(cancellationToken));
                if (doc.RootElement.TryGetProperty("name", out var nm)) d["category_name"] = nm;
                if (doc.RootElement.TryGetProperty("sub_categories", out var sc)) d["sub_categories"] = sc;
                if (doc.RootElement.TryGetProperty("active_sub_categories", out var asc)) d["active_sub_categories"] = asc;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Category fetch error for {Sid}", sid);
            }
        }

        private void ExtractTrademarkCountryUnit(JsonElement e, Dictionary<string, JsonElement> d)
        {
            if (e.TryGetProperty("trademark", out var t) && t.ValueKind == JsonValueKind.Object && t.TryGetProperty("name", out var tn))
                d["trademark_name"] = tn;
            if (e.TryGetProperty("country", out var c) && c.ValueKind == JsonValueKind.Object && c.TryGetProperty("name", out var cn))
                d["country_name"] = cn;
            if (e.TryGetProperty("unit", out var u) && u.ValueKind == JsonValueKind.Object && u.TryGetProperty("name", out var un))
                d["unit_name"] = un;
        }

        private void ExtractBarcodes(Dictionary<string, JsonElement> d)
        {
            if (!d.TryGetValue("barcodes", out var b) || b.ValueKind != JsonValueKind.Array) return;
            var s = string.Join(",", b.EnumerateArray().Select(x => x.GetString()));
            d["barcodes"] = JsonSerializer.SerializeToElement(s);
        }

        private void ExtractPhotos(JsonElement e, Dictionary<string, JsonElement> d)
        {
            if (e.TryGetProperty("photoIndexes", out var idx) &&
                e.TryGetProperty("photoVersions", out var ver) &&
                e.TryGetProperty("id", out var idElem) && idElem.ValueKind == JsonValueKind.Number)
            {
                var itemId = idElem.GetInt32();
                var map = ver.EnumerateArray()
                    .Select(x => new
                    {
                        Num = x.GetProperty("number").GetString() ?? string.Empty,
                        Ver = x.TryGetProperty("version", out var vp) && vp.ValueKind == JsonValueKind.Number ? vp.GetInt32()
                            : int.TryParse(vp.GetString(), out var tmp) ? tmp : 0
                    })
                    .ToDictionary(x => x.Num, x => x.Ver);

                var urls = idx.EnumerateArray()
                    .Select(i =>
                    {
                        var ns = i.GetString() ?? string.Empty;
                        return $"https://goods-photos.static1-sima-land.com/items/{itemId}/{ns}/700.jpg?v={map.GetValueOrDefault(ns)}";
                    })
                    .ToList();

                d["photo_urls"] = JsonSerializer.SerializeToElement(urls);
            }
            else if (e.TryGetProperty("base_photo_url", out var baseUrlElem) &&
                     e.TryGetProperty("agg_photos", out var aggElem))
            {
                var baseUrl = baseUrlElem.GetString() ?? "";
                if (!baseUrl.EndsWith("/")) baseUrl += "/";

                var urls = aggElem.EnumerateArray()
                    .Where(p => p.ValueKind == JsonValueKind.Number)
                    .Select(p => $"{baseUrl}{p.GetInt32()}/700.jpg")
                    .ToList();

                d["photo_urls"] = JsonSerializer.SerializeToElement(urls);
            }
        }

        private void ExtractAttributes(Dictionary<string, JsonElement> productDict, long sid, ConcurrentBag<product_attribute> bag)
        {
            if (!productDict.TryGetValue("attrs", out var attrsElem) || attrsElem.ValueKind != JsonValueKind.Array) return;

            foreach (var attr in attrsElem.EnumerateArray())
            {
                var attrName = attr.TryGetProperty("attr_name", out var an) ? an.GetString() ?? "" : "";
                var attrValue = attr.TryGetProperty("value", out var v) ? v.ToString() ?? "" : "";

                if (!string.IsNullOrWhiteSpace(attrName))
                {
                    bag.Add(new product_attribute
                    {
                        product_sid = sid,
                        attr_name = attrName,
                        value_text = attrValue
                    });
                }
            }
        }
    }
}
