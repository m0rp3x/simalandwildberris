using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WBSL.Models;

[Table("WbProductCardSizes")]
public class WbProductCardSize
{
    [Key, Column("ProductNmID", Order = 0)]
    public long ProductNmID { get; set; }

    [Key, Column("SizeChrtID", Order = 1)]
    public long SizeChrtID { get; set; }

    // Необязательно, но можно:
    // public WbProductCard Product { get; set; }
    // public WbSize Size { get; set; }
}