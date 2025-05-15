using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace WBSL.Models;

[Table("users")] // если используется схема, укажи Schema = "..."
public partial class user
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid id { get; set; }

    [Required]
    [Column("user_name")]
    [StringLength(100)]
    public string user_name { get; set; } = null!;

    [Column("email")]
    [StringLength(255)]
    public string? email { get; set; }

    [Required]
    [Column("password_hash")]
    [StringLength(255)]
    public string password_hash { get; set; } = null!;

    [Column("created_at")]
    public DateTime? created_at { get; set; }


    [Display(AutoGenerateField = false)]
    public virtual ICollection<external_account> external_accounts { get; set; } = new List<external_account>();
}