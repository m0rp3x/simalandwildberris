using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel;

namespace WBSL.Models;

[Table("WbProductCard")] // добавь Schema = "..." при необходимости
public partial class WbProductCard
{
    [Key]
    [Column("NmID")]
    public long NmID { get; set; }

    [Column("ImtID")]
    public long? ImtID { get; set; }

    [Column("NmUUID")]
    [StringLength(255)]
    public string? NmUUID { get; set; }

    [Column("SubjectID")]
    public int? SubjectID { get; set; }

    [Column("SubjectName")]
    [StringLength(255)]
    public string? SubjectName { get; set; }

    [Column("VendorCode")]
    [StringLength(255)]
    public string? VendorCode { get; set; }

    [Column("Brand")]
    [StringLength(255)]
    public string? Brand { get; set; }

    [Column("Title")]
    [StringLength(500)]
    public string? Title { get; set; }

    [Column("Description")]
    public string? Description { get; set; }

    [Column("NeedKiz")]
    public bool? NeedKiz { get; set; }

    [Column("CreatedAt")]
    public DateTime? CreatedAt { get; set; }

    [Column("UpdatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [Column("externalaccount_id")]
    public int? externalaccount_id { get; set; }

    [Column("LastSeenAt")]
    public DateTime? LastSeenAt { get; set; }

    [Display(AutoGenerateField = false)]
    public virtual ICollection<WbPhoto> WbPhotos { get; set; } = new List<WbPhoto>();

    [Display(AutoGenerateField = false)]
    public virtual ICollection<WbProductCardCharacteristic> WbProductCardCharacteristics { get; set; } = new List<WbProductCardCharacteristic>();

    [ForeignKey("externalaccount_id")]
    [Display(AutoGenerateField = false)]
    public virtual external_account? externalaccount { get; set; }

    [Display(AutoGenerateField = false)]
    public virtual ICollection<WbDimension> Dimensions { get; set; } = new List<WbDimension>();

    [Display(AutoGenerateField = false)]
    public virtual ICollection<WbSize> SizeChrts { get; set; } = new List<WbSize>();
}
