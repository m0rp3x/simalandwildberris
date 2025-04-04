using System;
using System.Collections.Generic;

namespace WBSL.Models;

public partial class wbphoto
{
    public long? wbproductcardnmid { get; set; }

    public string? big { get; set; }

    public string? c246x328 { get; set; }

    public string? c516x688 { get; set; }

    public string? hq { get; set; }

    public string? square { get; set; }

    public string? tm { get; set; }

    public virtual wbproductcard? wbproductcardnm { get; set; }
}
