using System;
using System.Collections.Generic;

namespace WBSL.Models;

public partial class wbproductcard
{
    public long nmid { get; set; }

    public long? imtid { get; set; }

    public string? nmuuid { get; set; }

    public int? subjectid { get; set; }

    public string? subjectname { get; set; }

    public string? vendorcode { get; set; }

    public string? brand { get; set; }

    public string? title { get; set; }

    public string? description { get; set; }

    public bool? needkiz { get; set; }

    public DateTime? createdat { get; set; }

    public DateTime? updatedat { get; set; }

    public virtual ICollection<wbcharacteristic> wbcharacteristics { get; set; } = new List<wbcharacteristic>();

    public virtual wbdimension? wbdimension { get; set; }

    public virtual ICollection<wbsize> wbsizes { get; set; } = new List<wbsize>();
}
