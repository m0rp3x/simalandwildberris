using System;
using System.Collections.Generic;

namespace WBSL.Models;

public partial class wbsku
{
    public long? wbsizechrtid { get; set; }

    public string? sku { get; set; }

    public virtual wbsize? wbsizechrt { get; set; }
}
