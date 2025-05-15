using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WBSL.Models;

[Table("products")]
public partial class product
{
    [Key]
    [Column("sid")]
    public long sid { get; set; }

    [Required]
    [Column("name")]
    [StringLength(255)]
    public string name { get; set; } = null!;

    [Column("description")]
    public string? description { get; set; }

    [Column("width")]
    public decimal? width { get; set; }

    [Column("height")]
    public decimal? height { get; set; }

    [Column("depth")]
    public decimal? depth { get; set; }

    [Column("weight")]
    public decimal? weight { get; set; }

    [Column("box_depth")]
    public decimal? box_depth { get; set; }

    [Column("box_height")]
    public decimal? box_height { get; set; }

    [Column("box_width")]
    public decimal? box_width { get; set; }

    [Column("category_id")]
    public int? category_id { get; set; }

    [Column("balance")]
    public int? balance { get; set; }

    [Column("qty_multiplier")]
    public int? qty_multiplier { get; set; }

    [Column("wholesale_price")]
    public decimal? wholesale_price { get; set; }

    [Column("price")]
    public decimal? price { get; set; }

    [Column("category_name")]
    [StringLength(255)]
    public string? category_name { get; set; }

    [Column("photo_urls")]
    [Display(AutoGenerateField = false)]
    public List<string>? photo_urls { get; set; }

    [Column("barcodes")]
    public string? barcodes { get; set; }

    [Column("vat")]
    public int? vat { get; set; }

    [Column("trademark_name")]
    [StringLength(255)]
    public string? trademark_name { get; set; }

    [Column("country_name")]
    [StringLength(255)]
    public string? country_name { get; set; }

    [Column("unit_name")]
    [StringLength(100)]
    public string? unit_name { get; set; }

    [Column("material_names")]
    public string material_names { get; set; } = "";

    [InverseProperty("product_s")]
    [Display(AutoGenerateField = false)]
    public virtual ICollection<product_attribute> product_attributes { get; set; } = new List<product_attribute>();
}
