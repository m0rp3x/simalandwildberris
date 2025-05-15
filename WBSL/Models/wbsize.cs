using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel;

namespace WBSL.Models;

[Table("WbSize")] // при необходимости добавь Schema = "..."
public partial class WbSize
{
    [Key]
    [Column("ChrtID")]
    public long ChrtID { get; set; }

    [Column("TechSize")]
    [StringLength(100)]
    public string? TechSize { get; set; }

    [Column("WbSize1")]
    [StringLength(100)]
    public string? WbSize1 { get; set; }

    [Column("Value")]
    [StringLength(255)]
    public string? Value { get; set; }

    [Display(AutoGenerateField = false)]
    public virtual ICollection<WbProductCard> ProductNms { get; set; } = new List<WbProductCard>();
}