using System.Text;
using System.Text.Json;
using WBSL.Data.Services.Wildberries.Models;

namespace WBSL.Data.Services.Wildberries;

public class WildberriesService : WildberriesBaseService
{
    public WildberriesService(IHttpClientFactory httpFactory) : base(httpFactory){
    }

    public async Task<WbProductCard?> GetProduct(string vendorCode)
    {
        try
        {
            var response = await GetProductByVendorCode(vendorCode);
            response.EnsureSuccessStatusCode();
        
            var apiResponse = await response.Content.ReadFromJsonAsync<WbApiResponse>();
            return apiResponse?.Cards?.FirstOrDefault();
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Ошибка при запросе продукта: {ex.Message}");
            return null;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Ошибка парсинга ответа: {ex.Message}");
            return null;
        }
    }
    
    private async Task<HttpResponseMessage> GetProductByVendorCode(string vendorCode){
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

        return await WbClient.PostAsync("/content/v2/get/cards/list", content);
    }
}