using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WBSL.Models;

namespace WBSL.Data.Models;

[Table("Orders")]
public class OrderEntity
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("address")]
    public string? Address { get; set; }

    [Column("user_id")]
    public int? UserId { get; set; }
    
    // Навигация, если хотите связать с User-таблицей:
    // [ForeignKey(nameof(UserId))]
    // public UserEntity? User { get; set; }
    
    [Column("sale_price")]
    public decimal SalePrice { get; set; }

    [Column("delivery_type")]
    public string DeliveryType { get; set; } = null!;
    
    [Column("comment")]
    public string Comment { get; set; } = "";
    
    [Column("order_uid")]
    public string OrderUid { get; set; } = null!;

    [Column("article")]
    public string Article { get; set; } = null!;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("warehouse_id")]
    public int WarehouseId { get; set; }
    
    [Column("nm_id")]
    public long NmId { get; set; }

    [ForeignKey(nameof(NmId))]
    public virtual WbProductCard? ProductCard { get; set; }
    
    [Column("chrt_id")]
    public long ChrtId { get; set; }

    [ForeignKey(nameof(ChrtId))]
    public virtual WbSize? Size { get; set; }
    
    [Column("price")]
    public decimal Price { get; set; }

    [Column("converted_price")]
    public decimal ConvertedPrice { get; set; }

    [Column("currency_code")]
    public int CurrencyCode { get; set; }

    [Column("converted_currency_code")]
    public int ConvertedCurrencyCode { get; set; }

    [Column("cargo_type")]
    public int CargoType { get; set; }

    [Column("is_zero_order")]
    public bool IsZeroOrder { get; set; }
    
    [Column("offices", TypeName = "jsonb")]
    public List<string> Offices { get; set; } = new();

    [Column("skus", TypeName = "jsonb")]
    public List<string> Skus { get; set; } = new();
    
    [Column("options_is_b2b")]
    public bool IsB2B { get; set; }
}