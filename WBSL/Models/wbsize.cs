using System;
using System.Collections.Generic;

namespace WBSL.Models;

public partial class wbsize
{
    public long chrtid { get; set; }

    public long? wbproductcardnmid { get; set; }

    public string? techsize { get; set; }

    public string? wbsize1 { get; set; }

    public virtual wbproductcard? wbproductcardnm { get; set; }
}
