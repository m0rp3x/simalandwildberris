namespace Shared;

public class WbItemApiResponse
{
    public WbProductFullInfoDto? wbProduct{ get; set; }
    public List<SimalandProductDto>? SimalandProducts { get; set; }
}
public class SimalandProductDto
{
    public long sid{ get; set; }
    public string name{ get; set; } = "";
    public string color{ get; set; } = "";
    public string description{ get; set; } = "";
    public decimal width{ get; set; }
    public decimal height{ get; set; }
    public decimal depth{ get; set; }
    public decimal weight{ get; set; }
    public decimal box_depth{ get; set; }
    public decimal box_height{ get; set; }
    public decimal box_width{ get; set; }
    public string base_photo_url{ get; set; } = "";
    public int category_id{ get; set; }
    public int? balance{ get; set; }
    public int qty_multiplier{ get; set; }
    public decimal wholesale_price{ get; set; }
    public decimal price{ get; set; }
    public string category_name{ get; set; } = "";
    public List<string> photo_urls{ get; set; } = new();
    public string? barcodes{ get; set; }
    public int? vat{ get; set; }
    public string? trademark_name{ get; set; }
    public string? country_name{ get; set; }
    public string? unit_name{ get; set; }
    public List<ProductAttribute> Attributes{ get; set; } = new();
}
public partial class ProductAttribute
{
    public int id { get; set; }

    public long product_sid { get; set; }

    public string attr_name { get; set; } = null!;

    public string? value_text { get; set; }

    public DateTime? created_at { get; set; }
}
