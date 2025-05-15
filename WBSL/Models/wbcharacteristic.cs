using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace WBSL.Models;

[Table("WbCharacteristic")] // если таблица в другой схеме — добавь Schema = "..."
public partial class WbCharacteristic
{
    [Key]
    [Column("Id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Column("Name")]
    [StringLength(255)]
    public string? Name { get; set; }

    [Column("Value")]
    [StringLength(1000)]
    public string? Value { get; set; }


    [Display(AutoGenerateField = false)]
    public virtual ICollection<WbProductCardCharacteristic> WbProductCardCharacteristics { get; set; } = new List<WbProductCardCharacteristic>();
}