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

    public virtual ICollection<WbCharacteristic> WbCharacteristics { get; set; } = new List<WbCharacteristic>();

    public virtual WbDimension? WbDimension { get; set; }

    public virtual ICollection<WbPhoto> WbPhotos { get; set; } = new List<WbPhoto>();

    public virtual ICollection<WbSize> WbSizes { get; set; } = new List<WbSize>();
}
