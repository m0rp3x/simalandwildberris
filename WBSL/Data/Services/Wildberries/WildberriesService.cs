using System.Text;
using System.Text.Json;
using Shared;
using WBSL.Data.HttpClientFactoryExt;
using WBSL.Data.Services.Wildberries.Models;

namespace WBSL.Data.Services.Wildberries;

public class WildberriesService : WildberriesBaseService
{
    private readonly QPlannerDbContext _db;

    public WildberriesService(PlatformHttpClientFactory httpFactory, QPlannerDbContext db) : base(httpFactory){
        _db = db;
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
            response.EnsureSuccessStatusCode();

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

    public async Task<WbApiResult> CreteWbItemsAsync(List<WbProductCardDto> itemsToCreate, int accountId){
        var wbClient = await GetWbClientAsync(accountId);
        
        var Limit = await GetWbLimitsAsync(wbClient);
        if (itemsToCreate.Count > Limit)
        {
            return new WbApiResult
            {
                Error = true,
                ErrorText = $"Превышен лимит создания карточек: доступно {Limit}, запрошено {itemsToCreate.Count}"
            };
        }
        var batches = SplitIntoCreateBatches(itemsToCreate);

        var allResults = new List<WbApiResult>();

        foreach (var batch in batches){
            var response = await SendCreateRequestAsync(batch, wbClient, accountId);
            await Task.Delay(TimeSpan.FromSeconds(2));

            var vendorCodes = batch.SelectMany(x => x.Variants).Select(y => y.VendorCode).ToList();
            var result = await ParseWbErrorsAsync(response, vendorCodes, wbClient);
            allResults.Add(result);
        }

        return MergeResults(allResults);
    }

    public async Task<WbApiResult> UpdateWbItemsAsync(List<WbProductCardDto> itemsToUpdate, int accountId){
        var WbClient = await GetWbClientAsync(accountId);
        var response = await SendUpdateRequestAsync(itemsToUpdate, WbClient, accountId);
        await Task.Delay(TimeSpan.FromSeconds(2));
        return await ParseWbErrorsAsync(response, itemsToUpdate.Select(x => x.VendorCode).ToList(), WbClient);
    }

    private async Task<List<WbAdditionalCharacteristicDto>?> GetProductChars(int? subjectId, int accountId){
        var WbClient = await GetWbClientAsync(accountId);
        var response = await WbClient.GetAsync($"/content/v2/object/charcs/{subjectId}");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions{ PropertyNameCaseInsensitive = true };
        var jsonDocument = JsonDocument.Parse(json);
        return JsonSerializer.Deserialize<List<WbAdditionalCharacteristicDto>>(
            jsonDocument.RootElement.GetProperty("data").GetRawText(), options);
    }

    private async Task<HttpResponseMessage> GetProductByVendorCode(string vendorCode, int accountId){
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
            WriteIndented = true // Для красивого форматирования (можно убрать в production)
        });

        // 3. Создаем контент запроса
        var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json"
        );
        var WbClient = await GetWbClientAsync(accountId);
        return await WbClient.PostAsync("/content/v2/get/cards/list", content);
    }
    public class WbLimitResponse
    {
        public WbLimitData Data { get; set; }
    }

    public class WbLimitData
    {
        public int FreeLimits { get; set; }
        public int PaidLimits { get; set; }
    }
    private async Task<int> GetWbLimitsAsync(HttpClient wbClient)
    {
        var response = await wbClient.GetAsync("/content/v2/cards/limits");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var limitResponse = JsonSerializer.Deserialize<WbLimitResponse>(content, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        return limitResponse?.Data?.FreeLimits ?? 0 + limitResponse?.Data?.PaidLimits ?? 0;
    }
    private async Task<WbApiResult> ParseWbErrorsAsync(WbUpdateResponse response, List<string> vendorCodes,
        HttpClient wbClient){
        var responseContent = await response.Response.Content.ReadAsStringAsync();

        try{
            var errorListResponse = await wbClient.GetAsync("/content/v2/cards/error/list");

            if (!errorListResponse.IsSuccessStatusCode){
                return new WbApiResult{
                    Error = true,
                    ErrorText = $"Не удалось получить ошибки из WB (StatusCode: {errorListResponse.StatusCode})",
                    AdditionalErrors = await errorListResponse.Content.ReadAsStringAsync()
                };
            }

            var errorListJson = await errorListResponse.Content.ReadAsStringAsync();
            var errorList = JsonSerializer.Deserialize<WbErrorListResponse>(errorListJson, new JsonSerializerOptions{
                PropertyNameCaseInsensitive = true
            });

            var matchedErrors = errorList?.Data?
                .Where(e =>
                    vendorCodes.Contains(e.VendorCode)) // фильтр по времени
                .ToList();

            if (matchedErrors != null && matchedErrors.Any()){
                var errorsDict = matchedErrors.ToDictionary(
                    e => e.VendorCode,
                    e => (object)e.Errors // каст к object для универсальности WbApiResult
                );

                return new WbApiResult{
                    Error = true,
                    ErrorText = "Ошибка при обновлении товаров",
                    AdditionalErrors = errorsDict
                };
            }

            return new WbApiResult{
                Error = false,
                ErrorText = "",
                AdditionalErrors = null
            };
        }
        catch{
            return new WbApiResult{
                Error = true,
                ErrorText = "Ошибка при разборе ответа от WB",
                AdditionalErrors = responseContent
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

    private async Task<WbUpdateResponse> SendCreateRequestAsync(List<WbProductGroup> batch, HttpClient wbClient,
        int? accountId){
        var json = JsonSerializer.Serialize(batch, new JsonSerializerOptions{
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await wbClient.PostAsync("/content/v2/cards/upload", content);
        response.EnsureSuccessStatusCode();

        return new WbUpdateResponse{
            Response = response,
            UpdateStartedAt = DateTime.UtcNow
        };
    }


    private WbApiResult MergeResults(List<WbApiResult> results){
        return new WbApiResult{
            Error = results.Any(r => r.Error),
            ErrorText = string.Join("; ", results.Where(r => r.Error).Select(r => r.ErrorText)),
            AdditionalErrors = results.Select(r => r.AdditionalErrors).ToList()
        };
    }

    private List<List<WbProductGroup>> SplitIntoCreateBatches(List<WbProductCardDto> allItems){
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
}