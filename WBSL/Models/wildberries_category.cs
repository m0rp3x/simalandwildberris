using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel;

namespace WBSL.Models;

[Table("wildberries_categories")] // добавь Schema = "..." при необходимости
public partial class wildberries_category
{
    [Key]
    [Column("id")]
    public int id { get; set; }

    [Required]
    [Column("parent_id")]
    [ForeignKey(nameof(parent))]
    public int parent_id { get; set; }

    [Required]
    [Column("name")]
    [StringLength(255)]
    public string name { get; set; } = null!;

    [Column("parent_name")]
    [StringLength(255)]
    public string? parent_name { get; set; }

    [Display(AutoGenerateField = false)]
    public virtual wildberries_parrent_category parent { get; set; } = null!;
}