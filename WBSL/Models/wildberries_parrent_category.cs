using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel;

namespace WBSL.Models;

[Table("wildberries_parrent_categories")] // добавь Schema = "..." при необходимости
public partial class wildberries_parrent_category
{
    [Key]
    [Column("id")]
    public int id { get; set; }

    [Required]
    [Column("name")]
    [StringLength(255)]
    public string name { get; set; } = null!;

    [Display(AutoGenerateField = false)]
    public virtual ICollection<wildberries_category> wildberries_categories { get; set; } = new List<wildberries_category>();
}