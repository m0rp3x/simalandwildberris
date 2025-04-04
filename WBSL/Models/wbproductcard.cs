using System;
using System.Collections.Generic;

namespace WBSL.Models;

public partial class WbProductCard
{
    public long NmID { get; set; }

    public long? ImtID { get; set; }

    public string? NmUUID { get; set; }

    public int? SubjectID { get; set; }

    public string? SubjectName { get; set; }

    public string? VendorCode { get; set; }

    public string? Brand { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public bool? NeedKiz { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<WbPhoto> WbPhotos { get; set; } = new List<WbPhoto>();

    public virtual ICollection<WbProductCardCharacteristic> WbProductCardCharacteristics { get; set; } = new List<WbProductCardCharacteristic>();

    public virtual ICollection<WbDimension> Dimensions { get; set; } = new List<WbDimension>();

    public virtual ICollection<WbSize> SizeChrts { get; set; } = new List<WbSize>();
}
