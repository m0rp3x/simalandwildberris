using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel;

namespace WBSL.Models;

[Table("WbProductCardCharacteristics")] // добавь Schema = "..." при необходимости
public partial class WbProductCardCharacteristic
{
    [Key]
    [Column(Order = 0)]
    [ForeignKey(nameof(ProductNm))]
    public long ProductNmID { get; set; }

    [Key]
    [Column(Order = 1)]
    [ForeignKey(nameof(Characteristic))]
    public int CharacteristicId { get; set; }

    [Column("Value")]
    [StringLength(1000)]
    public string? Value { get; set; }

    [Display(AutoGenerateField = false)]
    public virtual WbCharacteristic Characteristic { get; set; } = null!;

    [Display(AutoGenerateField = false)]
    public virtual WbProductCard ProductNm { get; set; } = null!;
}