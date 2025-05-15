using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WBSL.Models;

[Table("external_accounts")]
public partial class external_account
{
    [Key]
    [Column("id")]
    public int id { get; set; }

    [Required]
    [Column("user_id")]
    public Guid user_id { get; set; }

    [Required]
    [Column("platform")]
    [StringLength(100)]
    public string platform { get; set; } = null!;

    [Required]
    [Column("token")]
    public string token { get; set; } = null!;

    [Column("name")]
    [StringLength(100)]
    public string? name { get; set; }

    [Column("added_at")]
    public DateTime? added_at { get; set; }

    [Column("warehouseid")]
    public int? warehouseid { get; set; }

    [Display(AutoGenerateField = false)]
    public virtual ICollection<WbProductCard> WbProductCards { get; set; } = new List<WbProductCard>();

    [Display(AutoGenerateField = false)]
    public virtual user user { get; set; } = null!;
}