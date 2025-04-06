using System;
using System.Collections.Generic;

namespace WBSL.Models;

public partial class WbSize
{
    public long ChrtID { get; set; }

    public string? TechSize { get; set; }

    public string? WbSize1 { get; set; }

    public string? Value { get; set; }

    public virtual ICollection<WbProductCard> ProductNms { get; set; } = new List<WbProductCard>();
}
