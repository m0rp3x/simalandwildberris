using System;
using System.Collections.Generic;

namespace WBSL.Models;

public partial class wbcharacteristic
{
    public int id { get; set; }

    public long? wbproductcardnmid { get; set; }

    public string? name { get; set; }

    public string? value { get; set; }

    public virtual wbproductcard? wbproductcardnm { get; set; }
}
