using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shared;
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

    public WildberriesService(PlatformHttpClientFactory httpFactory, WildberriesProductsService productService,
        QPlannerDbContext db, IDbContextFactory<QPlannerDbContext> contextFactory) : base(httpFactory){
        _db = db;
        _contextFactory = contextFactory;
        _productService = productService;
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
            var apiProduct = apiResponse?.Cards?.FirstOrDefault();

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

    public async Task<WbApiExtendedResult> CreteWbItemsAsync(List<WbCreateVariantInternalDto> itemsToCreate,
        int accountId){
        if (itemsToCreate.Count == 0){
            return new WbApiExtendedResult{
                Result = new WbApiResult(),
                SuccessfulCount = 0
            };
        }

        var wbClient = await GetWbClientAsync(accountId);

        var batches = SplitIntoCreateBatches(itemsToCreate);
        var allResults = new List<WbApiResult>();
        var allSuccessVendorCodes = new List<string>();

        foreach (var batch in batches){
            var response = await SendCreateRequestAsync(batch, wbClient);
            await Task.Delay(TimeSpan.FromSeconds(2));

            var vendorCodes = batch.SelectMany(x => x.Variants).Select(y => y.VendorCode).ToList();
            var result = await ParseWbErrorsAsync(response, vendorCodes, wbClient);
            allResults.Add(result);

            if (!result.Error){
                allSuccessVendorCodes.AddRange(vendorCodes);
                var saveResult = await SearchAndAddSuccessfulAsync(vendorCodes, accountId);
                if (saveResult.Error){
                    allResults.Add(saveResult);
                }
            }
            else{
                if (result.AdditionalErrors != null && result.AdditionalErrors.Any()){
                    var failedVendorCodes = result.AdditionalErrors.Keys.ToHashSet();
                    var successVendorCodes = vendorCodes
                        .Where(v => !failedVendorCodes.Contains(v))
                        .ToList();

                    if (successVendorCodes.Any()){
                        allSuccessVendorCodes.AddRange(successVendorCodes);
                        var saveResult = await SearchAndAddSuccessfulAsync(successVendorCodes, accountId);
                        if (saveResult.Error){
                            allResults.Add(saveResult);
                        }
                    }
                }
            }
        }

        return new WbApiExtendedResult{
            Result = MergeResults(allResults),
            SuccessfulCount = allSuccessVendorCodes.Count,
            SuccessfulVendorCodes = allSuccessVendorCodes
        };
    }

    private async Task<WbApiResult?> CheckLimits(HttpClient wbClient, int accountId){
        WbApiResult wbApiResult;

        var maxLimit = await GetWbLimitsAsync(wbClient);

        var wbItemsCount = _db.WbProductCards.Count();

        if (wbItemsCount < maxLimit) return null;
        var Limit = maxLimit - wbItemsCount;
        {
            return (new WbApiResult{
                Error = true,
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
        await Task.Delay(5000);

        var fetchTasks = vendorCodes.Select(vendorCode => Task.Run(async () => {
            WbApiResponse? apiResponse = null;
            string? fetchError = null;

            for (int attempt = 1; attempt <= 3; attempt++){
                try{
                    var content = await CreateSearchRequestContentByVendorCode(textSearch: vendorCode);
                    var response = await wbClient.PostAsync("/content/v2/get/cards/list", content);
                    response.EnsureSuccessStatusCode();

                    apiResponse = await response.Content
                        .ReadFromJsonAsync<WbApiResponse>();

                    // если нашли карточку — выходим из retry
                    if (apiResponse?.Cards?.FirstOrDefault() is not null){
                        fetchError = null;
                        break;
                    }

                    fetchError = "Card not found, retrying…";
                }
                catch (Exception ex){
                    fetchError = $"Attempt {attempt} exception: {ex.Message}";
                }

                // delay перед следующей попыткой
                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            return (vendorCode, apiResponse, fetchError);
        })).ToList();

        var fetchResults = await Task.WhenAll(fetchTasks);

        var toProcess = new List<(string vendorCode, WbProductCard entity)>();
        var errors = new Dictionary<string, List<string>>();

        foreach (var (vendorCode, apiResponse, fetchError) in fetchResults){
            var card = apiResponse?.Cards?.FirstOrDefault();
            if (card != null){
                var entity = WbProductCardMapToDomain.MapToDomain(card);
                toProcess.Add((vendorCode, entity));
            }
            else{
                errors[vendorCode] = new List<string>{ fetchError ?? "Card not found after retries" };
            }
        }

        var sids = toProcess
            .Select(x => long.Parse(x.vendorCode))
            .ToList();

        var photosData = await _db.products
            .Where(p => sids.Contains(p.sid))
            .Select(p => new{ p.sid, p.photo_urls })
            .ToListAsync();

        var photoUrlsByVendor = photosData
            .ToDictionary(
                x => x.sid.ToString(),
                x => x.photo_urls ?? new List<string>()
            );

        var photoTasks = toProcess.Select(item => Task.Run(async () => {
            string? photoError = null;
            
            var urls = photoUrlsByVendor.TryGetValue(item.vendorCode, out var list)
                ? list
                : new List<string>();
            
            for (int attempt = 1; attempt <= 3; attempt++){
                var photoResult = await TrySendPhotosToWbAsync(
                    item.entity.NmID,
                    item.vendorCode,
                    urls,
                    wbClient);

                if (photoResult?.Error != true){
                    photoError = null;
                    break;
                }

                photoError = $"Photo upload error: {photoResult.ErrorText}";
                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            return (item.vendorCode, photoError);
        })).ToList();

        var photoResults = await Task.WhenAll(photoTasks);

        foreach (var (vendorCode, photoError) in photoResults){
            if (photoError != null){
                if (!errors.ContainsKey(vendorCode))
                    errors[vendorCode] = new List<string>();
                errors[vendorCode].Add(photoError);
            }
        }

        foreach (var (vendorCode, entity) in toProcess){
            try{
                await _productService
                    .SaveProductsToDatabaseAsync(
                        _db,
                        new List<WbProductCard>{ entity },
                        accountId);
            }
            catch (Exception ex){
                if (!errors.ContainsKey(vendorCode))
                    errors[vendorCode] = new List<string>();
                errors[vendorCode].Add($"DB save error: {ex.Message}");
            }
        }

        if (errors.Any()){
            return new WbApiResult{
                Error = true,
                ErrorText = "Ошибки при добавлении успешных карточек в БД",
                AdditionalErrors = errors
            };
        }

        return new WbApiResult{
            Error = false
        };
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
                Id = c.id,
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
                Id = c.id,
                Name = c.name
            })
            .Take(50).ToListAsync();

        return relatedCategories;
    }


    public async Task<List<WbCategoryDto>> SearchParentCategoriesAsync(string? query){
        var relatedCategories = await _db.wildberries_parrent_categories
            .Where(c => EF.Functions.ILike(c.name, $"%{query}%"))
            .Select(c => new WbCategoryDto{
                Id = c.id,
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

        var json = await response.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions{ PropertyNameCaseInsensitive = true };
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
                    withPhoto = -1
                }
            }
        };

        var json = JsonSerializer.Serialize(requestData, new JsonSerializerOptions{
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
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
        try{
            if (photoUrls.Count == 0)
                return null;

            var payload = new{ nmId, data = photoUrls };

            var content = JsonContent.Create(payload);
            var response = await wbClient.PostAsync("/content/v3/media/save", content);
            var apiResult = await response.Content.ReadFromJsonAsync<WbApiResult>();

            return apiResult?.Error == true
                ? apiResult
                : null;
        }
        catch (Exception ex){
            return new WbApiResult{
                Error = true,
                ErrorText = $"Exception during photo upload: {ex.Message}",
                AdditionalErrors = new Dictionary<string, List<string>>{
                    { vendorCode, new List<string>{ "Exception in TrySendPhotosToWbAsync" } }
                }
            };
        }
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

    private async Task<WbApiResult> ParseWbErrorsAsync(WbUpdateResponse response, List<string> vendorCodes,
        HttpClient wbClient){
        var responseContent = await response.Response.Content.ReadAsStringAsync();

        try{
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            var error = root.GetProperty("error").GetBoolean();
            var errorText = root.TryGetProperty("errorText", out var et) ? et.GetString() : null;

            if (error){
                var globalErrorDict = vendorCodes.ToDictionary(
                    v => v,
                    v => new List<string>{ errorText ?? "Unknown global error from WB" }
                );

                return new WbApiResult{
                    Error = true,
                    ErrorText = errorText,
                    AdditionalErrors = globalErrorDict
                };
            }

            var errorListResponse = await wbClient.GetAsync("/content/v2/cards/error/list");

            if (!errorListResponse.IsSuccessStatusCode){
                return new WbApiResult{
                    Error = false,
                    ErrorText = $"Не удалось получить ошибки из WB (StatusCode: {errorListResponse.StatusCode})",
                    AdditionalErrors = new Dictionary<string, List<string>>{
                        { "Global", new List<string>{ await errorListResponse.Content.ReadAsStringAsync() } }
                    }
                };
            }

            var errorListJson = await errorListResponse.Content.ReadAsStringAsync();
            using var doc2 = JsonDocument.Parse(errorListJson);
            if (doc2.RootElement.TryGetProperty("data", out var dataElement) &&
                dataElement.ValueKind != JsonValueKind.Array){
                return new WbApiResult{
                    Error = false,
                    ErrorText = null,
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
                            .OrderByDescending(e => e.UpdateAt) // сортировка по времени, свежие вверх
                            .First().Errors ?? new List<string>() // берём только одну последнюю ошибку
                    );

                return new WbApiResult{
                    Error = true,
                    ErrorText = "Ошибка при обновлении товаров",
                    AdditionalErrors = latestErrorsByVendor
                };
            }

            return new WbApiResult{
                Error = false
            };
        }
        catch (Exception ex){
            return new WbApiResult{
                Error = true,
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
            WriteIndented = false
        });

        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await WbClient.PostAsync("/content/v2/cards/update", content);
        return new WbUpdateResponse{
            Response = response,
            UpdateStartedAt = DateTime.UtcNow
        };
    }

    private async Task<WbUpdateResponse> SendCreateRequestAsync(List<WbProductGroup> batch, HttpClient wbClient){
        var json = JsonSerializer.Serialize(batch, new JsonSerializerOptions{
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await wbClient.PostAsync("/content/v2/cards/upload", content);

        return new WbUpdateResponse{
            Response = response,
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
            Error = results.Any(r => r.Error),
            ErrorText = string.Join("; ", results.Where(r => r.Error).Select(r => r.ErrorText)),
            AdditionalErrors = mergedErrors.Any() ? mergedErrors : null
        };
    }

    private List<List<WbProductGroup>> SplitIntoCreateBatches(List<WbCreateVariantInternalDto> allItems){
        const int maxGroupsPerRequest = 100;
        const int maxVariantsPerGroup = 30;
        const int maxSizeInBytes = 10 * 1024 * 1024;

        var grouped = allItems
            .GroupBy(x => x.SubjectID)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<List<WbProductGroup>>();
        var currentBatch = new List<WbProductGroup>();
        int currentSize = 0;

        foreach (var (subjectId, products) in grouped){
            // Делим по 30 вариантов максимум на группу
            var groupChunks = products
                .Chunk(maxVariantsPerGroup)
                .ToList();

            foreach (var chunk in groupChunks){
                var group = new WbProductGroup{
                    SubjectID = subjectId,
                    Variants = chunk.Select(dto => new WbCreateVariantDto(dto)).ToList()
                };

                var json = JsonSerializer.Serialize(group, new JsonSerializerOptions{
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                int size = Encoding.UTF8.GetByteCount(json);

                if (currentBatch.Count >= maxGroupsPerRequest || (currentSize + size > maxSizeInBytes)){
                    result.Add(currentBatch);
                    currentBatch = new List<WbProductGroup>();
                    currentSize = 0;
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
}