using System;
using System.Collections.Generic;

namespace WBSL.Models;

public partial class wbdimension
{
    public long wbproductcardnmid { get; set; }

    public int? width { get; set; }

    public int? height { get; set; }

    public int? length { get; set; }

    public double? weightbrutto { get; set; }

    public bool? isvalid { get; set; }

    public virtual wbproductcard wbproductcardnm { get; set; } = null!;
}
