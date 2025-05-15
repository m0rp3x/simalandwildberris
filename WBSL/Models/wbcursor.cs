using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WBSL.Models;

[Table("WbCursor")] // при необходимости добавь Schema = "..."
public partial class WbCursor
{
    [Key]
    [Column("Id")]
    public int Id { get; set; }

    [Column("UpdatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [Column("NmID")]
    public long? NmID { get; set; }

    [Column("Total")]
    public int? Total { get; set; }
}