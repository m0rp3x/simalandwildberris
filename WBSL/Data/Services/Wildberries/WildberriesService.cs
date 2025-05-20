using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Enums;
using WBSL.Data.Extensions;
using WBSL.Data.HttpClientFactoryExt;
using WBSL.Data.Mappers;
using WBSL.Data.Services.Wildberries.Models;
using WBSL.Models;

namespace WBSL.Data.Services.Wildberries;

public class WildberriesService : WildberriesBaseService
{
    private readonly QPlannerDbContext _db;
    private readonly IDbContextFactory<QPlannerDbContext> _contextFactory;
    private readonly WildberriesProductsService _productService;
    private readonly IServiceScopeFactory _scopeFactory;

    public WildberriesService(PlatformHttpClientFactory httpFactory, WildberriesProductsService productService, IServiceScopeFactory scopeFactory,
                              QPlannerDbContext db, IDbContextFactory<QPlannerDbContext> contextFactory) : base(
        httpFactory){
        _db             = db;
        _contextFactory = contextFactory;
        _productService = productService;
        _scopeFactory   = scopeFactory;
    }

    public async Task<WbProductFullInfoDto?> GetProduct(string vendorCode, int accountId){
        // var productFromDb = await _db.WbProductCards
        //     .Include(p => p.WbPhotos)
        //     .Include(p => p.WbProductCardCharacteristics)
        //     .ThenInclude(x => x.Characteristic)
        //     .Include(p => p.SizeChrts)
        //     .Include(x=>x.Dimensions)
        //     .FirstOrDefaultAsync(p => p.VendorCode == vendorCode);
        //
        // if (productFromDb != null){
        //     var productDto = WbProductCardMapper.MapToDto(productFromDb);
        //
        //     if (productFromDb.SubjectID.HasValue){
        //         var additionalCharacteristics = await GetProductChars(productFromDb.SubjectID);
        //         return new WbProductFullInfoDto(productDto, additionalCharacteristics);
        //     }
        //
        //     return new WbProductFullInfoDto(productDto);
        // }

        try{
            var response = await GetProductByVendorCode(vendorCode, accountId);

            var apiResponse = await response.Content.ReadFromJsonAsync<WbApiResponse>();
            var apiProduct  = apiResponse?.Cards?.FirstOrDefault();

            if (apiProduct == null) return null;

            // var productToSave = WbProductCardMapper.MapFromDto(apiProduct);
            // await _db.WbProductCards.AddAsync(productToSave);
            // await _db.SaveChangesAsync();

            if (apiProduct.SubjectID == 0) return new WbProductFullInfoDto(apiProduct);
            var additionalCharacteristics = await GetProductChars(apiProduct.SubjectID, accountId);
            return new WbProductFullInfoDto(apiProduct, additionalCharacteristics);
        }
        catch (HttpRequestException ex){
            Console.WriteLine($"Ошибка при запросе продукта: {ex.Message}");
            return null;
        }
        catch (JsonException ex){
            Console.WriteLine($"Ошибка парсинга ответа: {ex.Message}");
            return null;
        }
    }

    public async Task<WbProductCardDto?> GetProductWithOutCharacteristics(string vendorCode){
        try{
            using var db = _contextFactory.CreateDbContext();
            var productFromDb = await db.WbProductCards
                                        .FirstOrDefaultAsync(p => p.VendorCode == vendorCode);

            if (productFromDb != null){
                var productDto = WbProductCardMapper.MapToDto(productFromDb);

                return productDto;
            }

            return null;
        }
        catch (HttpRequestException ex){
            Console.WriteLine($"Ошибка при запросе продукта: {ex.Message}");
            return null;
        }
        catch (JsonException ex){
            Console.WriteLine($"Ошибка парсинга ответа: {ex.Message}");
            return null;
        }
    }

    public async Task<WbCreateApiExtendedResult> CreteWbItemsAsync(List<WbCreateVariantInternalDto> itemsToCreate,
                                                                   int accountId){
        if (itemsToCreate.Count == 0){
            return new WbCreateApiExtendedResult{
                Result          = new WbApiResult(),
                SuccessfulCount = 0
            };
        }

        var extended = new WbCreateApiExtendedResult();
        if (itemsToCreate.Count == 0)
            return extended;

        var wbClient = await GetWbClientAsync(accountId);

        var batches = SplitIntoCreateBatches(itemsToCreate);

        for (var i = 0; i < batches.Count; i++){
            var              batch       = batches[i];
            var              vendorCodes = batch.SelectMany(x => x.Variants).Select(y => y.VendorCode).ToList();
            WbUpdateResponse response;
            WbApiResult      result;
            try{
                response = await SendCreateRequestAsync(batch, wbClient);
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
            catch (HttpRequestException ex){
                // Сетевая ошибка или 5xx, поймали до парсинга
                extended.BatchErrors.Add(new WbCreateBatchError{
                    BatchIndex  = i,
                    VendorCodes = vendorCodes,
                    ErrorText   = $"Ошибка при создани WB: {ex.Message}"
                });
                continue;
            }

            if ((int)response.Response.StatusCode >= 500){
                var raw500 = await response.Response.Content.ReadAsStringAsync();
                extended.BatchErrors.Add(new WbCreateBatchError{
                    BatchIndex  = i,
                    VendorCodes = vendorCodes,
                    ErrorText   = $"WB вернул {response.Response.StatusCode}: {raw500}"
                });
                continue;
            }

            if (!response.Response.IsSuccessStatusCode){
                var raw = await response.Response.Content.ReadAsStringAsync();

                try{
                    using var doc  = JsonDocument.Parse(raw);
                    var       root = doc.RootElement;

                    // Если у нас стандартный WB-ответ с “error”: true
                    if (root.TryGetProperty("error", out var eProp) && eProp.GetBoolean()){
                        // Основной текст
                        var errorText = root.TryGetProperty("errorText", out var et)
                            ? et.GetString()
                            : $"WB вернул {(int)response.Response.StatusCode}";

                        // AdditionalErrors → Dictionary<string,List<string>>
                        var addErrs = new Dictionary<string, List<string>>();
                        if (root.TryGetProperty("additionalErrors", out var ae) &&
                            ae.ValueKind == JsonValueKind.Object){
                            foreach (var prop in ae.EnumerateObject()){
                                if (prop.Value.ValueKind == JsonValueKind.Array){
                                    var list = prop.Value
                                                   .EnumerateArray()
                                                   .Select(x => x.GetString() ?? "")
                                                   .ToList();
                                    addErrs[prop.Name] = list;
                                }
                            }
                        }

                        extended.BatchErrors.Add(new WbCreateBatchError{
                            BatchIndex       = i,
                            VendorCodes      = vendorCodes,
                            ErrorText        = errorText,
                            AdditionalErrors = addErrs
                        });
                        continue;
                    }
                }
                catch (JsonException){
                }

                // Fallback: сыро
                extended.BatchErrors.Add(new WbCreateBatchError{
                    BatchIndex  = i,
                    VendorCodes = vendorCodes,
                    ErrorText   = $"WB вернул {(int)response.Response.StatusCode}: {raw}"
                });
                continue;
            }

            result = await ParseWbErrorsAsync(response, vendorCodes, wbClient);

            if (result.Error){
                var mainText = result.ErrorText ?? "Неизвестная ошибка из WB";

                // 2) И захватим per-vendor словарь (может быть null)
                var addErrs = result.AdditionalErrors ??
                              new Dictionary<string, List<string>>();

                extended.BatchErrors.Add(new WbCreateBatchError{
                    BatchIndex       = i,
                    VendorCodes      = vendorCodes,
                    ErrorText        = mainText,
                    AdditionalErrors = addErrs
                });
            }

            var succeeded = !result.Error
                ? vendorCodes
                : vendorCodes.Except((IEnumerable<string>)result.AdditionalErrors?.Keys ?? Array.Empty<string>())
                             .ToList();

            extended.SuccessfulVendorCodes.AddRange(succeeded);

            if (succeeded.Any()){
                var saveResult = await SearchAndAddSuccessfulAsync(succeeded, accountId);
                if (saveResult.Error){
                    extended.BatchErrors.Add(new WbCreateBatchError{
                        BatchIndex  = i,
                        VendorCodes = succeeded,
                        ErrorText   = saveResult.ErrorText ?? "Ошибка при сохранении успешных карточек"
                    });
                }
                
                string jobId = await StartRetrySendPhotosJob(
                    succeeded,
                    accountId);
                Console.WriteLine(
                    $"[PhotoJob] batch #{i}: запущен jobId={jobId} " +
                    $"для {succeeded.Count} успешно созданных карточек");
            }
        }

        extended.Result = new WbApiResult{
            Error = extended.BatchErrors.Count > 0,
            ErrorText = extended.BatchErrors.Count > 0
                ? "Ошибки при загрузке одной или нескольких групп товаров"
                : null,
            AdditionalErrors = extended.BatchErrors
                                       .ToDictionary(
                                           be => $"Batch[{be.BatchIndex}]",
                                           be => new List<string>{ be.ErrorText }
                                       )
        };
        extended.SuccessfulCount = extended.SuccessfulVendorCodes.Count;
        return extended;
    }

    private async Task<WbApiResult?> CheckLimits(HttpClient wbClient, int accountId){
        WbApiResult wbApiResult;

        var maxLimit = await GetWbLimitsAsync(wbClient);

        var wbItemsCount = _db.WbProductCards.Count();

        if (wbItemsCount < maxLimit) return null;
        var Limit = maxLimit - wbItemsCount;
        {
            return (new WbApiResult{
                Error     = true,
                ErrorText = $"Превышен лимит создания карточек: доступно для загрузки {Limit}"
            });
        }
    }

    private async Task<List<long>> GetNmIdsByVendorCodes(List<string> vendorCodes){
        return await _db.WbProductCards
                        .Where(p => vendorCodes.Contains(p.VendorCode))
                        .Select(p => p.NmID)
                        .ToListAsync();
    }

    private async Task<WbApiResult> SearchAndAddSuccessfulAsync(List<string> vendorCodes, int accountId){
        var wbClient = await GetWbClientAsync(accountId);
        await Task.Delay(15000);
        var backoffs = new[]{
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(60)
        };
        const int maxAttempts    = 3;
        const int MaxParallelism = 5;
        var       throttler      = new SemaphoreSlim(MaxParallelism);
        var       errors         = new ConcurrentDictionary<string, List<string>>();

        var fetchTasks = vendorCodes.Select(async vendorCode => {
            await throttler.WaitAsync();
            
            try{
                WbApiResponse? apiResponse = null;
                string?        fetchError  = null;

                for (int attempt = 1; attempt <= maxAttempts; attempt++){
                    try{
                        var content  = await CreateSearchRequestContentByVendorCode(textSearch: vendorCode);
                        var response = await wbClient.PostAsync("/content/v2/get/cards/list", content);

                        if (response.StatusCode == (HttpStatusCode)429){
                            // Если 429 — ждём retry-after или наш backoff
                            var retryAfter = response.Headers.RetryAfter?.Delta
                                             ?? backoffs[Math.Min(attempt - 1, backoffs.Length - 1)];
                            fetchError = $"429 на попытке #{attempt}, ждём {retryAfter.TotalSeconds}s";
                            await Task.Delay(retryAfter);
                            continue;
                        }

                        response.EnsureSuccessStatusCode();

                        apiResponse = await response.Content
                                                    .ReadFromJsonAsync<WbApiResponse>();

                        if (apiResponse?.Cards?.Any() == true){
                            fetchError = null;
                            break;
                        }
                        
                        fetchError = $"Карточка не найдена (попытка #{attempt})";
                        if (attempt < maxAttempts)
                            await Task.Delay(backoffs[Math.Min(attempt - 1, backoffs.Length - 1)]);
                        
                    }
                    catch (Exception ex){
                        fetchError = $"Ошибка на попытке #{attempt}: {ex.Message}";
                        if (attempt < maxAttempts)
                            await Task.Delay(TimeSpan.FromSeconds(5));
                    }
                }
                
                if (apiResponse?.Cards?.FirstOrDefault() is { } card)
                {
                    var entity = WbProductCardMapToDomain.MapToDomain(card);
                    try
                    {
                        await _productService.SaveProductsToDatabaseAsync(
                            _db,
                            new List<WbProductCard> { entity },
                            accountId,
                            DateTime.UtcNow);
                    }
                    catch (Exception ex)
                    {
                        fetchError = $"Ошибка при сохранении в БД: {ex.Message}";
                    }
                }
                
                if (fetchError != null)
                    errors[vendorCode] = new List<string> { fetchError };
            }
            finally{
                throttler.Release();
            }
        }).ToList();

        await Task.WhenAll(fetchTasks);

        if (errors.Any())
        {
            return new WbApiResult
            {
                Error            = true,
                ErrorText        = "Ошибки при добавлении успешных карточек в БД",
                AdditionalErrors = errors.ToDictionary(kv => kv.Key, kv => kv.Value)
            };
        }

        return new WbApiResult { Error = false };
    }

    public async Task<string> StartRetrySendPhotosJob(
        IEnumerable<string> vendorCodes,
        int accountId,
        CancellationToken cancellationToken = default){
        List<(string VendorCode, long NmId, List<string> PhotoUrls)> toProcess;
        using (var db = await _contextFactory.CreateDbContextAsync(cancellationToken)){
            var cards = await db.WbProductCards
                                .AsNoTracking()
                                .Where(c => vendorCodes.Contains(c.VendorCode))
                                .Select(c => new{
                                    c.VendorCode,
                                    c.NmID
                                })
                                .ToListAsync(cancellationToken);

            var codes = vendorCodes
                        .Select(vc => long.TryParse(vc, out var n) ? (long?)n : null)
                        .Where(n => n.HasValue)
                        .Select(n => n!.Value)
                        .ToList();
            var products = await db.products
                                   .AsNoTracking()
                                   .Where(p => codes.Contains(p.sid))
                                   .Select(p => new{ p.sid, p.photo_urls })
                                   .ToListAsync(cancellationToken);
            toProcess = cards
                        .Select(c => {
                            var photos = products
                                         .FirstOrDefault(p => p.sid.ToString() == c.VendorCode)
                                         ?.photo_urls
                                         ?? new List<string>();

                            return (
                                VendorCode: c.VendorCode!,
                                NmId: c.NmID,
                                PhotoUrls: photos.OrderBy(GetPhotoIndex).ToList()
                            );
                        })
                        // отбрасываем все, у кого нет фото
                        .Where(x => x.PhotoUrls.Any())
                        .ToList();
        }

        var jobId           = ProgressStore.CreateJob(toProcess.Count);
        var scopeFactory    = _scopeFactory;
        _ = Task.Run(async () => {
            using var bgScope = scopeFactory.CreateScope();

            try{
                var errors = new Dictionary<string, List<string>>();
                var count  = 0;
                var clientFactory = bgScope.ServiceProvider
                                           .GetRequiredService<PlatformHttpClientFactory>();

                var client = await clientFactory.GetValidClientAsync(
                    ExternalAccountType.Wildberries,
                    new[]{ accountId },
                    "/ping", // или "/api/ping", как у вас
                    CancellationToken.None );

                if (client == null)
                    throw new InvalidOperationException(
                        $"Не удалось получить рабочий WB-клиент для accountId={accountId}");

                var throttler = new SemaphoreSlim(4);
                var tasks = toProcess.Select(async item => {
                    await throttler.WaitAsync(cancellationToken);
                    try{
                        var res = await TrySendPhotosToWbAsync(
                            item.NmId, item.VendorCode, item.PhotoUrls, client);
                        if (res?.Error == true)
                            errors[item.VendorCode] =
                                res.AdditionalErrors?.SelectMany(kv => kv.Value).ToList()
                                ?? new List<string>{ res.ErrorText! };
                    }
                    finally{
                        Interlocked.Increment(ref count);
                        ProgressStore.UpdateProgress(jobId);
                        throttler.Release();
                    }
                }).ToList();

                await Task.WhenAll(tasks);

                // 3) Собираем итоговый WbApiResult:
                var total   = toProcess.Count;
                var failed  = errors.Count;
                var success = total - failed;
                var result = new WbApiResult{
                    Error            = failed > 0,
                    ErrorText        = failed > 0 ? "Часть фото не загрузилась" : "",
                    AdditionalErrors = failed > 0 ? errors : null,
                    TotalCount       = total,
                    SuccessCount     = success,
                    ErrorCount       = failed
                };

                ProgressStore.CompleteJob(jobId, result);
            }
            catch (Exception ex){
                Console.WriteLine(ex.Message);
                ProgressStore.FailJob(jobId);
            }
        }, cancellationToken);

        return jobId.ToString();
    }

    public async Task<List<WbCategoryDto>> SearchCategoriesAsync(string? query, int baseSubjectId){
        var baseCategory = await _db.wildberries_categories
                                    .FirstOrDefaultAsync(c => c.id == baseSubjectId);

        if (baseCategory == null)
            return new List<WbCategoryDto>();

        var parentId = baseCategory.parent_id;

        // 2. Ищем все категории с тем же parent_id и по совпадению в названии
        var relatedCategories = await _db.wildberries_categories
                                         .Where(c => c.parent_id == parentId &&
                                                     EF.Functions.ILike(c.name, $"%{query}%"))
                                         .Select(c => new WbCategoryDto{
                                             Id   = c.id,
                                             Name = c.name
                                         })
                                         .ToListAsync();

        return relatedCategories;
    }

    public async Task<List<WbCategoryDto>> SearchCategoriesByParentIdAsync(string? query, int parentId){
        var relatedCategories = await _db.wildberries_categories
                                         .Where(c => c.parent_id == parentId &&
                                                     EF.Functions.ILike(c.name, $"%{query}%"))
                                         .Select(c => new WbCategoryDto{
                                             Id   = c.id,
                                             Name = c.name
                                         })
                                         .Take(50).ToListAsync();

        return relatedCategories;
    }


    public async Task<List<WbCategoryDto>> SearchParentCategoriesAsync(string? query){
        var relatedCategories = await _db.wildberries_parrent_categories
                                         .Where(c => EF.Functions.ILike(c.name, $"%{query}%"))
                                         .Select(c => new WbCategoryDto{
                                             Id   = c.id,
                                             Name = c.name
                                         })
                                         .ToListAsync();

        return relatedCategories;
    }

    public async Task<WbApiResult> UpdateWbItemsAsync(List<WbProductCardDto> itemsToUpdate, int accountId){
        var WbClient = await GetWbClientAsync(accountId);
        var response = await SendUpdateRequestAsync(itemsToUpdate, WbClient, accountId);
        await Task.Delay(TimeSpan.FromSeconds(2));
        return await ParseWbErrorsAsync(response, itemsToUpdate.Select(x => x.VendorCode).ToList(), WbClient);
    }

    public async Task<List<WbAdditionalCharacteristicDto>?> GetProductChars(int? subjectId, int accountId){
        var WbClient = await GetWbClientAsync(accountId);
        var response = await WbClient.GetAsync($"/content/v2/object/charcs/{subjectId}");
        response.EnsureSuccessStatusCode();

        var json         = await response.Content.ReadAsStringAsync();
        var options      = new JsonSerializerOptions{ PropertyNameCaseInsensitive = true };
        var jsonDocument = JsonDocument.Parse(json);
        return JsonSerializer.Deserialize<List<WbAdditionalCharacteristicDto>>(
            jsonDocument.RootElement.GetProperty("data").GetRawText(), options);
    }

    private async Task<HttpResponseMessage> GetProductByVendorCode(string vendorCode, int accountId, int maxRetries = 3,
                                                                   int delayMs = 3000){
        var requestData = new{
            settings = new{
                cursor = new{
                    limit = 1
                },
                filter = new{
                    textSearch = vendorCode,
                    withPhoto  = -1
                }
            }
        };

        var json = JsonSerializer.Serialize(requestData, new JsonSerializerOptions{
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented        = true
        });

        // 3. Создаем контент запроса
        var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json"
        );
        int attempt = 0;
        while (attempt < maxRetries){
            var WbClient = await GetWbClientAsync(accountId);
            var response = await WbClient.PostAsync("/content/v2/get/cards/list", content);

            if (response.IsSuccessStatusCode)
                return response;

            var body = await response.Content.ReadAsStringAsync();

            if ((int)response.StatusCode == 500 && body.Contains("\"error\": true")){
                Console.WriteLine($"[WB Retry] Попытка {attempt + 1} — 500 с ошибкой: {body}");
                await Task.Delay(delayMs);
                attempt++;
            }
            else{
                // Не 500 или без error — сразу бросаем
                response.EnsureSuccessStatusCode();
            }
        }

        throw new HttpRequestException($"WB API: не удалось получить ответ после {maxRetries} попыток");
    }

    public class WbLimitResponse
    {
        public WbLimitData Data{ get; set; }
    }

    public class WbLimitData
    {
        public int FreeLimits{ get; set; }
        public int PaidLimits{ get; set; }
    }

    private async Task<WbApiResult?> TrySendPhotosToWbAsync(
        long nmId,
        string vendorCode,
        List<string> photoUrls,
        HttpClient wbClient){
        if (photoUrls.Count == 0)
            return null;

        const int maxAttempts = 3;
        int       attempt     = 0;

        while (true){
            try{
                var payload = new{ nmId, data = photoUrls };
                var content = JsonContent.Create(payload);

                var response = await wbClient.PostAsync("/content/v3/media/save", content);

                if (response.StatusCode == (HttpStatusCode)429){
                    var delay = response.Headers.RetryAfter?.Delta
                                ?? TimeSpan.FromSeconds(5);
                    await Task.Delay(delay);
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.Forbidden){
                    var apiResult = await response.Content
                                                  .ReadFromJsonAsync<WbApiResult>()
                                    // на всякий случай, если парсинг вернул null
                                    ?? new WbApiResult{
                                        Error     = true,
                                        ErrorText = "Forbidden: empty response"
                                    };
                    return apiResult;
                }

                response.EnsureSuccessStatusCode();

                var successResult = await response.Content
                                                  .ReadFromJsonAsync<WbApiResult>();

                return successResult?.Error == true
                    ? successResult
                    : null;
            }
            catch (HttpRequestException) when (++attempt < maxAttempts){
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex){
                return new WbApiResult{
                    Error     = true,
                    ErrorText = $"Exception during photo upload: {ex.Message}",
                    AdditionalErrors = new Dictionary<string, List<string>>{
                        { vendorCode, new List<string>{ ex.GetType().Name } }
                    }
                };
            }

            if (attempt >= maxAttempts)
                break;
        }

        return new WbApiResult{
            Error     = true,
            ErrorText = $"Failed after {maxAttempts} attempts",
            AdditionalErrors = new Dictionary<string, List<string>>{
                { vendorCode, new List<string>{ "Max retries reached" } }
            }
        };
    }

    private async Task<int> GetWbLimitsAsync(HttpClient wbClient){
        var response = await wbClient.GetAsync("/content/v2/cards/limits");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var limitResponse = JsonSerializer.Deserialize<WbLimitResponse>(content, new JsonSerializerOptions{
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        return limitResponse?.Data?.FreeLimits ?? 0 + limitResponse?.Data?.PaidLimits ?? 0;
    }

    private async Task<WbApiResult> ParseWbErrorsAsync(WbUpdateResponse response,
                                                       List<string> vendorCodes,
                                                       HttpClient wbClient){
        var responseContent = await response.Response.Content.ReadAsStringAsync();

        try{
            using var doc  = JsonDocument.Parse(responseContent);
            var       root = doc.RootElement;

            var error     = root.GetProperty("error").GetBoolean();
            var errorText = root.TryGetProperty("errorText", out var et) ? et.GetString() : null;

            if (error){
                var globalErrorDict = vendorCodes.ToDictionary(
                    v => v,
                    v => new List<string>{ errorText ?? "Unknown global error from WB" }
                );

                return new WbApiResult{
                    Error            = true,
                    ErrorText        = errorText,
                    AdditionalErrors = globalErrorDict
                };
            }

            var errorListResponse = await wbClient.GetAsync("/content/v2/cards/error/list");

            if (!errorListResponse.IsSuccessStatusCode){
                return new WbApiResult{
                    Error     = false,
                    ErrorText = $"Не удалось получить ошибки из WB (StatusCode: {errorListResponse.StatusCode})",
                    AdditionalErrors = new Dictionary<string, List<string>>{
                        { "Global", new List<string>{ await errorListResponse.Content.ReadAsStringAsync() } }
                    }
                };
            }

            var       errorListJson = await errorListResponse.Content.ReadAsStringAsync();
            using var doc2          = JsonDocument.Parse(errorListJson);
            if (doc2.RootElement.TryGetProperty("data", out var dataElement) &&
                dataElement.ValueKind != JsonValueKind.Array){
                return new WbApiResult{
                    Error            = false,
                    ErrorText        = null,
                    AdditionalErrors = null
                };
            }

            var errorList = JsonSerializer.Deserialize<WbErrorListResponse>(errorListJson, new JsonSerializerOptions{
                PropertyNameCaseInsensitive = true
            });

            var matchedErrors = errorList?.Data?
                                         .Where(e =>
                                                    vendorCodes.Contains(e.VendorCode)) // фильтр по времени
                                         .ToList();

            if (matchedErrors != null && matchedErrors.Any()){
                var latestErrorsByVendor = matchedErrors
                                           .GroupBy(e => e.VendorCode)
                                           .ToDictionary(
                                               g => g.Key,
                                               g => g
                                                    .OrderByDescending(
                                                        e => e.UpdateAt) // сортировка по времени, свежие вверх
                                                    .First().Errors ??
                                                    new List<string>() // берём только одну последнюю ошибку
                                           );

                return new WbApiResult{
                    Error            = true,
                    ErrorText        = "Ошибка при обновлении товаров",
                    AdditionalErrors = latestErrorsByVendor
                };
            }

            return new WbApiResult{
                Error = false
            };
        }
        catch (Exception ex){
            return new WbApiResult{
                Error     = true,
                ErrorText = "Ошибка при разборе ответа от WB",
                AdditionalErrors = new Dictionary<string, List<string>>{
                    { "Exception", new List<string>{ ex.Message, responseContent } }
                }
            };
        }
    }

    public class WbUpdateResponse
    {
        public HttpResponseMessage Response{ get; set; }
        public DateTime UpdateStartedAt{ get; set; }
    }

    public class WbProductGroup
    {
        public int SubjectID{ get; set; }
        public List<WbCreateVariantDto> Variants{ get; set; } = new();
    }

    private async Task<WbUpdateResponse> SendUpdateRequestAsync(List<WbProductCardDto> itemsToUpdate,
                                                                HttpClient WbClient, int? accountId = null){
        var json = JsonSerializer.Serialize(itemsToUpdate, new JsonSerializerOptions{
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented        = false
        });

        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await WbClient.PostAsync("/content/v2/cards/update", content);
        return new WbUpdateResponse{
            Response        = response,
            UpdateStartedAt = DateTime.UtcNow
        };
    }

    private async Task<WbUpdateResponse> SendCreateRequestAsync(List<WbProductGroup> batch, HttpClient wbClient){
        var json = JsonSerializer.Serialize(batch, new JsonSerializerOptions{
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented        = false
        });

        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await wbClient.PostAsync("/content/v2/cards/upload", content);

        return new WbUpdateResponse{
            Response        = response,
            UpdateStartedAt = DateTime.UtcNow
        };
    }


    private WbApiResult MergeResults(List<WbApiResult> results){
        var mergedErrors = new Dictionary<string, List<string>>();

        foreach (var result in results){
            if (result.AdditionalErrors == null) continue;

            foreach (var pair in result.AdditionalErrors){
                if (!mergedErrors.ContainsKey(pair.Key))
                    mergedErrors[pair.Key] = new List<string>();

                mergedErrors[pair.Key].AddRange(pair.Value);
            }
        }

        return new WbApiResult{
            Error            = results.Any(r => r.Error),
            ErrorText        = string.Join("; ", results.Where(r => r.Error).Select(r => r.ErrorText)),
            AdditionalErrors = mergedErrors.Any() ? mergedErrors : null
        };
    }

    private List<List<WbProductGroup>> SplitIntoCreateBatches(List<WbCreateVariantInternalDto> allItems){
        const int maxGroupsPerRequest = 100;
        const int maxVariantsPerGroup = 30;
        const int maxSizeInBytes      = 10 * 1024 * 1024;

        var grouped = allItems
                      .GroupBy(x => x.SubjectID)
                      .ToDictionary(g => g.Key, g => g.ToList());

        var result       = new List<List<WbProductGroup>>();
        var currentBatch = new List<WbProductGroup>();
        int currentSize  = 0;

        foreach (var (subjectId, products) in grouped){
            // Делим по 30 вариантов максимум на группу
            var groupChunks = products
                              .Chunk(maxVariantsPerGroup)
                              .ToList();

            foreach (var chunk in groupChunks){
                var group = new WbProductGroup{
                    SubjectID = subjectId,
                    Variants  = chunk.Select(dto => new WbCreateVariantDto(dto)).ToList()
                };

                var json = JsonSerializer.Serialize(group, new JsonSerializerOptions{
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                int size = Encoding.UTF8.GetByteCount(json);

                if (currentBatch.Count >= maxGroupsPerRequest || (currentSize + size > maxSizeInBytes)){
                    result.Add(currentBatch);
                    currentBatch = new List<WbProductGroup>();
                    currentSize  = 0;
                }

                currentBatch.Add(group);
                currentSize += size;
            }
        }

        if (currentBatch.Any())
            result.Add(currentBatch);

        return result;
    }

    private async Task<StringContent>
        CreateSearchRequestContentByVendorCode(string updatedAt = "", long? nmID = null, string? textSearch = null){
        var cursor = new Dictionary<string, object>{
            ["limit"] = 100
        };

        if (!string.IsNullOrEmpty(updatedAt)){
            cursor["updatedAt"] = updatedAt;
        }

        if (nmID.HasValue){
            cursor["nmID"] = nmID.Value;
        }

        var filter = new Dictionary<string, object>{
            ["withPhoto"] = -1
        };
        if (!string.IsNullOrWhiteSpace(textSearch)){
            filter["textSearch"] = textSearch;
        }

        var requestData = new Dictionary<string, object>{
            ["settings"] = new{
                cursor = cursor,
                filter = filter
            }
        };

        var json = JsonSerializer.Serialize(requestData);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    int GetPhotoIndex(string url){
        const string marker = "/items/";

        int pos = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (pos < 0)
            return int.MaxValue;

        pos += marker.Length; // встали на цифру id
        // пропускаем сам id
        int slashAfterId = url.IndexOf('/', pos);
        if (slashAfterId < 0)
            return int.MaxValue;

        int indexStart = slashAfterId + 1; // начало индекса
        // ищем конец – либо следующий '/', либо начало query-string
        int slashAfterIndex = url.IndexOf('/', indexStart);
        int qPos            = url.IndexOfAny(new[]{ '?', '&' }, indexStart);
        int end = slashAfterIndex > 0
            ? slashAfterIndex
            : (qPos > 0
                ? qPos
                : url.Length);

        int len = end - indexStart;
        if (len <= 0)
            return int.MaxValue;

        string idxStr = url.Substring(indexStart, len);
        return int.TryParse(idxStr, out var idx)
            ? idx
            : int.MaxValue;
    }
}