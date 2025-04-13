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

    public async Task<WbProductFullInfoDto?> GetProduct(string vendorCode, int? accountId = null){
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
            var additionalCharacteristics = await GetProductChars(apiProduct.SubjectID);
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
    
    public async Task<WbApiResult> UpdateWbItemsAsync(List<WbProductCardDto> itemsToUpdate, int? accountId = null)
    {
        var response = await SendUpdateRequestAsync(itemsToUpdate, accountId);
        return await ParseWbResponseAsync(response);
    }

    private async Task<List<WbAdditionalCharacteristicDto>?> GetProductChars(int? subjectId){
        var WbClient = await GetWbClientAsync();
        var response = await WbClient.GetAsync($"/content/v2/object/charcs/{subjectId}");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions{ PropertyNameCaseInsensitive = true };
        var jsonDocument = JsonDocument.Parse(json);
        return JsonSerializer.Deserialize<List<WbAdditionalCharacteristicDto>>(
            jsonDocument.RootElement.GetProperty("data").GetRawText(), options);
    }

    private async Task<HttpResponseMessage> GetProductByVendorCode(string vendorCode, int? accountId = null){
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

    private async Task<WbApiResult> ParseWbResponseAsync(HttpResponseMessage response)
    {
        var responseContent = await response.Content.ReadAsStringAsync();

        try
        {
            using var document = JsonDocument.Parse(responseContent);
            var root = document.RootElement;

            return new WbApiResult
            {
                Error = root.GetProperty("error").GetBoolean(),
                ErrorText = root.GetProperty("errorText").GetString(),
                AdditionalErrors = root.TryGetProperty("additionalErrors", out var additional)
                    ? JsonSerializer.Deserialize<object>(additional.GetRawText())
                    : null
            };
        }
        catch
        {
            return new WbApiResult
            {
                Error = true,
                ErrorText = "Ошибка при разборе ответа от WB",
                AdditionalErrors = responseContent
            };
        }
    }

    private async Task<HttpResponseMessage> SendUpdateRequestAsync(List<WbProductCardDto> itemsToUpdate, int? accountId = null)
    {
        var json = JsonSerializer.Serialize(itemsToUpdate, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var WbClient = await GetWbClientAsync(accountId);
        return await WbClient.PostAsync("/content/v2/cards/update", content);
    }

}