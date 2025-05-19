using System.Text.Json.Serialization;

namespace WBSL.Data.Models.DTO;

public class OrderDto
{
    [JsonPropertyName("supplyId")]
    public string? SupplyId { get; set; }  
    
    [JsonPropertyName("address")]
    public AddressDto? Address { get; set; } 

    [JsonPropertyName("userId")]
    public int? UserId { get; set; }

    [JsonPropertyName("salePrice")]
    public decimal SalePrice { get; set; }

    [JsonPropertyName("requiredMeta")]
    public List<string> RequiredMeta { get; set; } = new();

    [JsonPropertyName("deliveryType")]
    public string DeliveryType { get; set; } = null!;

    [JsonPropertyName("comment")]
    public string Comment { get; set; } = "";

    [JsonPropertyName("scanPrice")]
    public decimal? ScanPrice { get; set; }

    [JsonPropertyName("orderUid")]
    public string OrderUid { get; set; } = null!;

    [JsonPropertyName("article")]
    public string Article { get; set; } = null!;

    [JsonPropertyName("colorCode")]
    public string ColorCode { get; set; } = "";

    [JsonPropertyName("rid")]
    public string Rid { get; set; } = null!;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("offices")]
    public List<string> Offices { get; set; } = new();

    [JsonPropertyName("skus")]
    public List<string> Skus { get; set; } = new();

    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("warehouseId")]
    public int WarehouseId { get; set; }

    [JsonPropertyName("nmId")]
    public long NmId { get; set; }

    [JsonPropertyName("chrtId")]
    public long ChrtId { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("convertedPrice")]
    public decimal ConvertedPrice { get; set; }

    [JsonPropertyName("currencyCode")]
    public int CurrencyCode { get; set; }

    [JsonPropertyName("convertedCurrencyCode")]
    public int ConvertedCurrencyCode { get; set; }

    [JsonPropertyName("cargoType")]
    public int CargoType { get; set; }

    [JsonPropertyName("isZeroOrder")]
    public bool IsZeroOrder { get; set; }

    [JsonPropertyName("options")]
    public OrderOptionsDto Options { get; set; } = new();
}

public class AddressDto
{
    [JsonPropertyName("fullAddress")]
    public string FullAddress { get; set; } = null!;

    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }

    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    public override string ToString(){
        return FullAddress + "|" + Latitude + "|" + Longitude;
    }
}