using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace WBSL.Models;

[Table("product_attributes")]
public partial class product_attribute
{
    [Key]
    [Column("id")]
    public int id { get; set; }

    [Column("product_sid")]
    [JsonPropertyName("product_s")]
    public long product_sid { get; set; }

    [Column("attr_name")]
    [StringLength(255)]
    [JsonPropertyName("attr_name")]
    public string attr_name { get; set; } = null!;

    [Column("value_text")]
    [StringLength(1000)]
    [JsonPropertyName("value_text")]
    public string? value_text { get; set; }

    [Column("created_at")]
    public DateTime? created_at { get; set; }

    [JsonIgnore]
    [BindNever]
    [ValidateNever]
    [ForeignKey("product_sid")]
    [Display(AutoGenerateField = false)]
    public virtual product? product_s { get; set; }
}