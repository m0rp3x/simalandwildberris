using System.ComponentModel.DataAnnotations;

namespace Shared;

public class WbItemApiResponse
{
    public WbProductFullInfoDto? wbProduct{ get; set; }
    public List<SimalandProductDto>? SimalandProducts { get; set; }
}
public class SimalandProductDto
{
    public long sid{ get; set; }
    [Display(Name = "Название")]
    public string name{ get; set; } = "";

    [Display(Name = "Описание")]
    public string description{ get; set; } = "";
    [Display(Name = "Ширина, см")]
    public decimal width{ get; set; }
    [Display(Name = "Высота, см")]
    public decimal height{ get; set; }
    [Display(Name = "Глубина, см")]
    public decimal depth{ get; set; }
    [Display(Name = "Вес, кг")]
    public decimal weight{ get; set; }
    [Display(Name = "Глубина коробки")]
    public decimal box_depth{ get; set; }
    [Display(Name = "Высота коробки")]
    public decimal box_height{ get; set; }
    [Display(Name = "Ширина коробки")]
    public decimal box_width{ get; set; }
    [Display(Name = "Базовый URL фото")]
    public string base_photo_url{ get; set; } = "";
    public int category_id{ get; set; }
    [Display(Name = "Остаток")]
    public int? balance{ get; set; }
    [Display(Name = "Кратность")]
    public int qty_multiplier{ get; set; }
    [Display(Name = "Оптовая цена")]
    public decimal wholesale_price{ get; set; }
    [Display(Name = "Цена")]
    public decimal price{ get; set; }
    [Display(Name = "Название категории")]
    public string category_name{ get; set; } = "";
    [Display(Name = "Фото")]
    public List<string> photo_urls{ get; set; } = new();
    [Display(Name = "Штрихкоды")]
    public string? barcodes{ get; set; }
    [Display(Name = "НДС")]
    public int? vat{ get; set; }
    [Display(Name = "Торговая марка")]
    public string? trademark_name{ get; set; }
    [Display(Name = "Страна")]
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
