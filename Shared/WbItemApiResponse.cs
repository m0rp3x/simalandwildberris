namespace Shared;

public class WbItemApiResponse
{
    public WbProductFullInfoDto? wbProduct{ get; set; }
    public List<SimalandProductDto>? SimalandProducts { get; set; }
    public List<ProductAttribute>? Attributes { get; set; }
}
public class SimalandProductDto
{
    // Определите свойства, которые ожидаете получить
    public long Sid { get; set; }
    public string? Name { get; set; }
    public decimal Price { get; set; }
    // ... другие поля
}
public partial class ProductAttribute
{
    public int id { get; set; }

    public long product_sid { get; set; }

    public string attr_name { get; set; } = null!;

    public string? value_text { get; set; }

    public DateTime? created_at { get; set; }
}
