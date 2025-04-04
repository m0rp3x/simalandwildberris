using System.Text;
using System.Text.Json;

namespace WBSL.Data.Services;

public class WildberriesService
{
    private HttpClient WbClient => _httpFactory.CreateClient("WildBerries");
    private readonly IHttpClientFactory _httpFactory;

    public WildberriesService(IHttpClientFactory httpFactory){
        _httpFactory = httpFactory;
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
    
    public class WbApiResponse
    {
        public List<WbProductCard> Cards { get; set; }
        public WbCursor Cursor { get; set; }
    }
    
    public class WbProductCard
    {
        public long NmID { get; set; }
        public long ImtID { get; set; }
        public string NmUUID { get; set; }
        public int SubjectID { get; set; }
        public string SubjectName { get; set; }
        public string VendorCode { get; set; }
        public string Brand { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public bool NeedKiz { get; set; }
        public List<WbPhoto> Photos { get; set; }
        public WbDimensions Dimensions { get; set; }
        public List<WbCharacteristic> Characteristics { get; set; }
        public List<Wbsize> Sizes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class WbPhoto
    {
        public string Big { get; set; }
        public string C246x328 { get; set; }
        public string C516x688 { get; set; }
        public string Hq { get; set; }
        public string Square { get; set; }
        public string Tm { get; set; }
    }

    public class WbDimensions
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int Length { get; set; }
        public double WeightBrutto { get; set; }
        public bool IsValid { get; set; }
    }

    public class WbCharacteristic
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public object Value { get; set; } // Может быть string, double или List<string>
    }

    public class Wbsize
    {
        public long ChrtID { get; set; }
        public string TechSize { get; set; }
        public string WbSize { get; set; }
        public List<string> Skus { get; set; }
    }

    public class WbCursor
    {
        public DateTime UpdatedAt { get; set; }
        public long NmID { get; set; }
        public int Total { get; set; }
    }
}