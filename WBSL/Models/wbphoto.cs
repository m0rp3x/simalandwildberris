using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace WBSL.Models;

[Table("WbPhoto")] // при необходимости добавь Schema = "..."
public partial class WbPhoto
{
    [Key]
    [Column("Id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [Column("WbProductCardNmID")]
    public long WbProductCardNmID { get; set; }

    [Column("Big")]
    [StringLength(1000)]
    public string? Big { get; set; }

    [Column("C246x328")]
    [StringLength(1000)]
    public string? C246x328 { get; set; }

    [Column("C516x688")]
    [StringLength(1000)]
    public string? C516x688 { get; set; }

    [Column("Hq")]
    [StringLength(1000)]
    public string? Hq { get; set; }

    [Column("Square")]
    [StringLength(1000)]
    public string? Square { get; set; }

    [Column("Tm")]
    [StringLength(1000)]
    public string? Tm { get; set; }


    [ForeignKey("WbProductCardNmID")]
    [Display(AutoGenerateField = false)]
    public virtual WbProductCard WbProductCardNm { get; set; } = null!;
}