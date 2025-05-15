using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace WBSL.Models;

[Table("WbDimensions")] // добавь Schema = "..." при необходимости
public partial class WbDimension
{
    [Key]
    [Column("Id")]
    public int Id { get; set; }

    [Column("Width")]
    public int? Width { get; set; }

    [Column("Height")]
    public int? Height { get; set; }

    [Column("Length")]
    public int? Length { get; set; }

    [Column("WeightBrutto")]
    public double? WeightBrutto { get; set; }

    [Column("IsValid")]
    public bool? IsValid { get; set; }

 
    [Display(AutoGenerateField = false)]
    public virtual ICollection<WbProductCard> ProductNms { get; set; } = new List<WbProductCard>();
}