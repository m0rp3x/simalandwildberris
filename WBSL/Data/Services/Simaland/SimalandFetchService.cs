using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Shared;

namespace WBSL.Data.Services.SimaLand;

public class SimaLandFetchService
{
    private readonly HttpClient _client;

    public SimaLandFetchService(IHttpClientFactory factory, IConfiguration config)
    {
        _client = factory.CreateClient("SimaLand");
        _client.DefaultRequestHeaders.Add("X-Api-Key", config["SimaLand:ApiKey"]!);
    }

    public async Task<List<SimaProductDto>> GetProductsBySids(List<long> sids)
    {
        var results = new List<SimaProductDto>();

        foreach (var sid in sids)
        {
            var response = await _client.GetAsync($"item/?sid={sid}&expand=description,stocks,barcodes,attrs,category,trademark,country,unit,category_id");
            if (!response.IsSuccessStatusCode) continue;

            var json = await response.Content.ReadAsStringAsync();
            var root = JsonDocument.Parse(json).RootElement;
            var items = root.GetProperty("items");
            if (items.GetArrayLength() == 0) continue;

            var product = items[0];
            var dto = new SimaProductDto
            {
                Sid = product.GetProperty("sid").GetInt64(),
                Name = product.GetProperty("name").GetString() ?? "",
                Width = product.GetProperty("width").GetDecimal(),
                Height = product.GetProperty("height").GetDecimal(),
                Depth = product.GetProperty("depth").GetDecimal(),
                Weight = product.GetProperty("weight").GetDecimal(),
                BoxDepth = product.GetProperty("box_depth").GetDecimal(),
                BoxHeight = product.GetProperty("box_height").GetDecimal(),
                BoxWidth = product.GetProperty("box_width").GetDecimal(),
                CategoryId = product.GetProperty("category_id").GetInt32(),
                QtyMultiplier = product.GetProperty("qty_multiplier").GetInt32(),
                WholesalePrice = product.GetProperty("wholesale_price").GetDecimal(),
                Price = product.GetProperty("price").GetDecimal(),
                Balance = product.TryGetProperty("balance", out var b) && b.ValueKind == JsonValueKind.Number ? b.GetInt32() : null
            };

            if (product.TryGetProperty("description", out var desc))
            {
                var raw = desc.GetString() ?? "";
                dto.Description = Regex.Replace(raw, "<.*?>", string.Empty);
            }

            if (product.TryGetProperty("barcodes", out var barcodes))
            {
                var all = barcodes.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x));
                dto.Barcodes = string.Join(",", all);
            }

            if (product.TryGetProperty("category", out var cat) && cat.TryGetProperty("name", out var cn))
                dto.CategoryName = cn.GetString();
            if (product.TryGetProperty("trademark", out var tm) && tm.TryGetProperty("name", out var tn))
                dto.TrademarkName = tn.GetString();
            if (product.TryGetProperty("country", out var co) && co.TryGetProperty("name", out var con))
                dto.CountryName = con.GetString();
            if (product.TryGetProperty("unit", out var un) && un.TryGetProperty("name", out var unn))
                dto.UnitName = unn.GetString();

            if (product.TryGetProperty("photoIndexes", out var indexesElem) &&
                product.TryGetProperty("photoVersions", out var versionsElem))
            {
                var itemId = product.GetProperty("id").GetInt32();
                var versions = versionsElem.EnumerateArray()
                    .Where(x => x.TryGetProperty("number", out _) && x.TryGetProperty("version", out _))
                    .ToDictionary(x => x.GetProperty("number").GetString() ?? "0", x =>
                        x.GetProperty("version").GetInt32());

                dto.PhotoUrls = indexesElem.EnumerateArray().Select(indexElem =>
                {
                    var index = indexElem.GetString() ?? "0";
                    var version = versions.TryGetValue(index, out var ver) ? ver : 0;
                    return $"https://goods-photos.static1-sima-land.com/items/{itemId}/{index}/700.jpg?v={version}";
                }).ToList();
            }

            if (product.TryGetProperty("attrs", out var attrsElem))
            {
                foreach (var attr in attrsElem.EnumerateArray())
                {
                    var name = attr.TryGetProperty("attr_name", out var an) ? an.GetString() ?? "" : "";
                    var value = attr.TryGetProperty("value", out var v) ? v.ToString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        dto.Attributes.Add(new SimaProductAttributeDto
                        {
                            AttrName = name,
                            Value = value
                        });
                    }
                }
            }

            results.Add(dto);
        }

        return results;
    }
}
