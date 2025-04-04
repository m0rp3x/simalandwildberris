using System;
using System.Collections.Generic;

namespace WBSL.Models;

public partial class WbSize
{
    public long ChrtID { get; set; }

    public long WbProductCardNmID { get; set; }

    public string? TechSize { get; set; }

    public string? WbSize1 { get; set; }

    public virtual WbProductCard WbProductCardNm { get; set; } = null!;

    public virtual ICollection<WbSku> WbSkus { get; set; } = new List<WbSku>();
}
