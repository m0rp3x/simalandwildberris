using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WBSL.Models;

namespace WBSL.Data.Services.Simaland;

public class SimalandFetchService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly QPlannerDbContext _db;
    private readonly IConfiguration _config;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SimalandFetchService(IHttpClientFactory httpFactory, QPlannerDbContext db, IConfiguration config, IHttpContextAccessor httpContextAccessor)
    {
        _httpFactory = httpFactory;
        _db = db;
        _config = config;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<(List<JsonElement> Products, List<product_attribute> Attributes)> FetchProductsAsync(int accountId, List<long> articleSids)
    {
        Guid userId = Guid.Empty;
        var user = _httpContextAccessor.HttpContext?.User;
        if (user != null){
            userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty);
        }
        else{
            throw new InvalidOperationException("Пользователь не найден.");
        }
        var account = await _db.external_accounts
            .FirstOrDefaultAsync(a => a.id == accountId && a.user_id == userId && a.platform == "SimaLand");

        if (account == null)
            throw new InvalidOperationException("Аккаунт не найден или не принадлежит пользователю.");

        var client = _httpFactory.CreateClient("SimaLand");
        client.DefaultRequestHeaders.Add("X-Api-Key", account.token);

        var results = new List<JsonElement>();
        var allAttributes = new List<product_attribute>();

        foreach (var sid in articleSids)
        {
            var response = await client.GetAsync($"item/?sid={sid}&expand=description,stocks,barcodes,attrs,category,trademark,country,unit,category_id");

            if (!response.IsSuccessStatusCode)
                continue;

            var json = await response.Content.ReadAsStringAsync();
            var root = JsonSerializer.Deserialize<JsonElement>(json);
            var items = root.GetProperty("items");

            if (items.GetArrayLength() == 0) continue;

            var product = items[0];
            var productDict = product.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

            if (productDict.TryGetValue("description", out var descElement))
            {
                var rawDesc = descElement.GetString() ?? "";
                var cleanDesc = Regex.Replace(rawDesc, "<.*?>", string.Empty);
                productDict["description"] = JsonSerializer.SerializeToElement(cleanDesc);
            }

            if (product.TryGetProperty("category_id", out var categoryIdElem))
            {
                var categoryId = categoryIdElem.GetInt32();
                var categoryResponse = await client.GetAsync($"category/{categoryId}/?expand=sub_categories");
                if (categoryResponse.IsSuccessStatusCode)
                {
                    var categoryJson = await categoryResponse.Content.ReadAsStringAsync();
                    var categoryObj = JsonSerializer.Deserialize<JsonElement>(categoryJson);
                    if (categoryObj.TryGetProperty("name", out var nameElem))
                        productDict["category_name"] = nameElem;

                    if (categoryObj.TryGetProperty("sub_categories", out var subCatsElem))
                    {
                        var subNames = subCatsElem.EnumerateArray()
                            .Where(c => c.TryGetProperty("name", out _))
                            .Select(c => c.GetProperty("name").GetString())
                            .ToList();
                        productDict["sub_category_names"] = JsonSerializer.SerializeToElement(subNames);
                    }
                }
            }

            if (product.TryGetProperty("trademark", out var trademarkProp) && trademarkProp.ValueKind == JsonValueKind.Object && trademarkProp.TryGetProperty("name", out var trademarkName))
                productDict["trademark_name"] = trademarkName;

            if (product.TryGetProperty("country", out var countryProp) && countryProp.ValueKind == JsonValueKind.Object && countryProp.TryGetProperty("name", out var countryName))
                productDict["country_name"] = countryName;

            if (product.TryGetProperty("unit", out var unitProp) && unitProp.ValueKind == JsonValueKind.Object && unitProp.TryGetProperty("name", out var unitName))
                productDict["unit_name"] = unitName;

            if (productDict.TryGetValue("barcodes", out var barcodesElement))
            {
                var barcodes = barcodesElement.EnumerateArray().Select(b => b.GetString()).Where(s => s != null).ToList();
                productDict["barcodes"] = JsonSerializer.SerializeToElement(string.Join(",", barcodes));
            }

            if (product.TryGetProperty("photoIndexes", out var indexesElem) && product.TryGetProperty("photoVersions", out var versionsElem))
            {
                var itemId = product.GetProperty("id").GetInt32();
                var versions = new Dictionary<string, int>();

                foreach (var versionElem in versionsElem.EnumerateArray())
                {
                    var number = versionElem.GetProperty("number").GetString() ?? "0";
                    if (versionElem.TryGetProperty("version", out var verJson) && verJson.ValueKind == JsonValueKind.Number)
                        versions[number] = verJson.GetInt32();
                }

                var photoUrls = indexesElem.EnumerateArray()
                    .Select(indexElem =>
                    {
                        var index = indexElem.GetString() ?? "0";
                        var version = versions.TryGetValue(index, out var ver) ? ver : 0;
                        return $"https://goods-photos.static1-sima-land.com/items/{itemId}/{index}/700.jpg?v={version}";
                    })
                    .ToList();

                productDict["photo_urls"] = JsonSerializer.SerializeToElement(photoUrls);
            }
            else if (product.TryGetProperty("base_photo_url", out var baseUrlElem) && product.TryGetProperty("agg_photos", out var aggElem))
            {
                var baseUrl = baseUrlElem.GetString() ?? "";
                if (!baseUrl.EndsWith("/")) baseUrl += "/";

                var urls = aggElem.EnumerateArray()
                    .Where(p => p.ValueKind == JsonValueKind.Number)
                    .Select(p => $"{baseUrl}{p.GetInt32()}/700.jpg")
                    .ToList();

                productDict["photo_urls"] = JsonSerializer.SerializeToElement(urls);
            }

            if (product.TryGetProperty("attrs", out var attrsElem) && attrsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var attr in attrsElem.EnumerateArray())
                {
                    var attrId = attr.GetProperty("attr_id").GetInt32();
                    var attrName = attr.TryGetProperty("attr_name", out var an) ? an.GetString() ?? "" : "";
                    var attrValue = attr.TryGetProperty("value", out var v) ? v.ToString() ?? "" : "";

                    allAttributes.Add(new product_attribute
                    {
                        product_sid = sid,
                        attr_name = attrName,
                        value_text = attrValue
                    });
                }
            }

            var updatedJson = JsonSerializer.SerializeToElement(productDict);
            results.Add(updatedJson);
        }

        return (results, allAttributes);
    }
}
