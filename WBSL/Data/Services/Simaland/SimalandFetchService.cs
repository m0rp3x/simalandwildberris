using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Shared;
using WBSL.Models;

namespace WBSL.Data.Services.Simaland;

public class SimalandFetchService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly QPlannerDbContext _db;
    private readonly IConfiguration _config;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SimalandFetchService(IHttpClientFactory httpFactory, QPlannerDbContext db, IConfiguration config,
        IHttpContextAccessor httpContextAccessor){
        _httpFactory = httpFactory;
        _db = db;
        _config = config;
        _httpContextAccessor = httpContextAccessor;
    }
    public async Task<List<SimalandProductDto>> FetchProductsWithMergedAttributesAsync(int accountId, List<long> articleSids)
    {
        var products = await FetchProductsAsync(accountId, articleSids);

        foreach (var product in products)
        {
            product.Attributes = MergeDuplicateAttributes(product.Attributes, product.sid);
        }

        return products;
    }
    
    
    public async Task<List<SimalandProductDto>>
        FetchProductsAsync(int accountId, List<long> articleSids){
        // Аутентификация и проверка аккаунта
        var userId = GetCurrentUserId();
        var account = await VerifyAccountAsync(accountId, userId);

        var client = CreateSimalandClient(account.token);
        var results = new List<SimalandProductDto>();

        foreach (var sid in articleSids){
            try{
                var response =
                    await client.GetAsync(
                        $"item/?sid={sid}&expand=description,stocks,barcodes,attrs,category,trademark,country,unit,category_id");
                if (!response.IsSuccessStatusCode) continue;

                var json = await response.Content.ReadAsStringAsync();
                var product = await ProcessProductResponse(client, json, sid);

                results.Add(product);
            }
            catch (Exception ex){
                Console.WriteLine($"Error processing product {sid}");
            }
        }

        return results;
    }
    
    private List<ProductAttribute> MergeDuplicateAttributes(List<ProductAttribute> attributes, long sid)
    {
        return attributes
            .GroupBy(attr => attr.id)
            .Select(group => new ProductAttribute
            {
                product_sid = sid,
                id = group.Key,
                attr_name = group.First().attr_name,
                value_text = string.Join("; ", group
                    .Select(a => a.value_text)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct())
            })
            .ToList();
    }
    private async Task<SimalandProductDto?> ProcessProductResponse(HttpClient client,
        string json, long sid){
        var root = JsonDocument.Parse(json).RootElement;
        var items = root.GetProperty("items");
        if (items.GetArrayLength() == 0) return null;

        var product = items[0];
        var dto = new SimalandProductDto();

        // Базовые свойства
        dto.sid = sid;
        dto.name = product.TryGetProperty("name", out var name) ? name.GetString() : string.Empty;
        dto.description = product.TryGetProperty("description", out var desc)
            ? Regex.Replace(desc.GetString() ?? "", "<.*?>", string.Empty)
            : string.Empty;
        dto.price = product.TryGetProperty("price", out var price) ? price.GetDecimal() : 0;
        dto.balance = product.TryGetProperty("stocks", out var stocks) && stocks.GetArrayLength() > 0
            ? stocks[0].GetProperty("balance").GetInt32()
            : 0;
        
        dto.width = product.TryGetProperty("width", out var widthProp) && widthProp.TryGetDecimal(out var width)
            ? Math.Round(width, 3)
            : 0;

        dto.height = product.TryGetProperty("height", out var heightProp) && heightProp.TryGetDecimal(out var height)
            ? Math.Round(height, 3)
            : 0;

        dto.depth = product.TryGetProperty("depth", out var depthProp) && depthProp.TryGetDecimal(out var depth)
            ? Math.Round(depth, 3)
            : 0;

        dto.weight = product.TryGetProperty("weight", out var weightProp) && weightProp.TryGetDecimal(out var weight)
            ? Math.Round(weight / 1000m, 3)
            : 0;

        dto.box_width = product.TryGetProperty("box_width", out var boxWidthProp) && boxWidthProp.TryGetDecimal(out var boxWidth)
            ? Math.Round(boxWidth, 3)
            : 0;

        dto.box_height = product.TryGetProperty("box_height", out var boxHeightProp) && boxHeightProp.TryGetDecimal(out var boxHeight)
            ? Math.Round(boxHeight, 3)
            : 0;

        dto.box_depth = product.TryGetProperty("box_depth", out var boxDepthProp) && boxDepthProp.TryGetDecimal(out var boxDepth)
            ? Math.Round(boxDepth, 3)
            : 0;

        dto.wholesale_price = product.TryGetProperty("wholesale_price", out var wsPriceProp) && wsPriceProp.TryGetDecimal(out var wsPrice)
            ? Math.Round(wsPrice, 3)
            : 0;
        dto.vat = product.TryGetProperty("vat", out var vatProp) && vatProp.TryGetInt32(out var vat) ? vat : null;

        // Категория
        if (product.TryGetProperty("category_id", out var categoryId)){
            dto.category_id = categoryId.ValueKind == JsonValueKind.Number
                ? categoryId.GetInt32()
                : int.TryParse(categoryId.GetString(), out var parsedId)
                    ? parsedId
                    : 0;
            var categoryResponse = await client.GetAsync($"category/{dto.category_id}/?expand=sub_categories");
            if (categoryResponse.IsSuccessStatusCode){
                var categoryJson = await categoryResponse.Content.ReadAsStringAsync();
                var category = JsonSerializer.Deserialize<JsonElement>(categoryJson);
                dto.category_name = category.TryGetProperty("name", out var catName) ? catName.GetString() : null;
            }
        }

        // Фотографии
        // dto.photo_urls = GetPhotoUrls(product);
        // dto.base_photo_url = product.TryGetProperty("base_photo_url", out var baseUrl) ? baseUrl.GetString() : null;

        // Штрихкоды
        if (product.TryGetProperty("barcodes", out var barcodes)){
            dto.barcodes = string.Join(",", barcodes.EnumerateArray().Select(b => b.GetString()));
        }

        // Дополнительные поля
        dto.trademark = GetNestedProperty(product, "trademark", "name");
        dto.country_name = GetNestedProperty(product, "country", "name");
        dto.unit_name = GetNestedProperty(product, "unit", "name");

        // Атрибуты
        if (product.TryGetProperty("attrs", out var attrs)){
            foreach (var attr in attrs.EnumerateArray()){
                var attrId = attr.TryGetProperty("attr_id", out var id) ? id.GetInt32() : 0;
                var attrName = attr.TryGetProperty("attr_name", out var an) ? an.GetString() : null;
                var attrValue = attr.TryGetProperty("value", out var av) ? av.ToString() : null;

                if (!string.IsNullOrEmpty(attrName)){
                    dto.Attributes.Add(new ProductAttribute{
                        id = attrId,
                        product_sid = sid,
                        attr_name = attrName,
                        value_text = attrValue
                    });
                }
            }
        }

        return dto;
    }

    private List<string> GetPhotoUrls(JsonElement product){
        var urls = new List<string>();

        // Первый вариант фото (photoIndexes + photoVersions)
        if (product.TryGetProperty("photoIndexes", out var indexes) &&
            product.TryGetProperty("photoVersions", out var versions)){
            try{
                var itemId = product.GetProperty("id").GetInt32();
                var versionDict = new Dictionary<string, int>();

                foreach (var versionElem in versions.EnumerateArray()){
                    var number = versionElem.TryGetProperty("number", out var numProp)
                        ? numProp.GetString() ?? "0"
                        : "0";

                    if (versionElem.TryGetProperty("version", out var verProp)){
                        var version = verProp.ValueKind == JsonValueKind.Number
                            ? verProp.GetInt32()
                            : int.TryParse(verProp.GetString(), out var parsed)
                                ? parsed
                                : 0;
                        versionDict[number] = version;
                    }
                }

                foreach (var indexElem in indexes.EnumerateArray()){
                    var index = indexElem.ValueKind == JsonValueKind.Number
                        ? indexElem.GetInt32().ToString()
                        : indexElem.GetString() ?? "0";

                    var version = versionDict.TryGetValue(index, out var ver) ? ver : 0;
                    urls.Add($"https://goods-photos.static1-sima-land.com/items/{itemId}/{index}/700.jpg?v={version}");
                }
            }
            catch (Exception ex){
                Console.WriteLine("Error processing photoIndexes/photoVersions");
            }
        }
        // Второй вариант фото (base_photo_url + agg_photos)
        else if (product.TryGetProperty("base_photo_url", out var baseUrl) &&
                 product.TryGetProperty("agg_photos", out var aggPhotos)){
            try{
                var baseUrlStr = baseUrl.GetString() ?? "";
                if (!baseUrlStr.EndsWith("/")) baseUrlStr += "/";

                foreach (var photoElem in aggPhotos.EnumerateArray()){
                    var photoId = photoElem.ValueKind == JsonValueKind.Number
                        ? photoElem.GetInt32().ToString()
                        : photoElem.GetString() ?? "0";

                    urls.Add($"{baseUrlStr}{photoId}/700.jpg");
                }
            }
            catch (Exception ex){
                Console.WriteLine("Error processing base_photo_url/agg_photos");
            }
        }

        return urls;
    }

    private string GetNestedProperty(JsonElement element, string objectName, string propertyName){
        if (element.TryGetProperty(objectName, out var obj) &&
            obj.ValueKind == JsonValueKind.Object &&
            obj.TryGetProperty(propertyName, out var prop)){
            return prop.GetString();
        }

        return null;
    }

    private Guid GetCurrentUserId(){
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null) throw new InvalidOperationException("Пользователь не найден.");
        return Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty);
    }

    private async Task<external_account> VerifyAccountAsync(int accountId, Guid userId){
        var account = await _db.external_accounts
            .FirstOrDefaultAsync(a => a.id == accountId && a.user_id == userId && a.platform == "SimaLand");
        return account ?? throw new InvalidOperationException("Аккаунт не найден или не принадлежит пользователю.");
    }

    private HttpClient CreateSimalandClient(string token){
        var client = _httpFactory.CreateClient("SimaLand");
        client.DefaultRequestHeaders.Add("X-Api-Key", token);
        return client;
    }
}