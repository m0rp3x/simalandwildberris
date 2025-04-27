using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace WBSL.Models;

public partial class product_attribute
{
    public int id { get; set; }
    [JsonPropertyName("product_s")]     // поле JSON → это свойство
    public long product_sid { get; set; }
    [JsonPropertyName("attr_name")]
    public string attr_name { get; set; } = null!;
    [JsonPropertyName("value_text")]
    public string? value_text { get; set; }

    public DateTime? created_at { get; set; }
    
    [JsonIgnore]
    [BindNever]        
    [ValidateNever]     
    public virtual product? product_s { get; set; }  // стала nullable
}