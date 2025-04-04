using System;
using System.Collections.Generic;

namespace WBSL.Models;

public partial class WbSize
{
    public long ChrtID { get; set; }

    public string? TechSize { get; set; }

    public string? WbSize1 { get; set; }

    public virtual ICollection<WbSku> WbSkus { get; set; } = new List<WbSku>();

    public virtual ICollection<WbProductCard> ProductNms { get; set; } = new List<WbProductCard>();
}
