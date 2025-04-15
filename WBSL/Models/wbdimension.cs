using System;
using System.Collections.Generic;

namespace WBSL.Models;

public partial class WbDimension
{
    public int Id { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public int? Length { get; set; }

    public decimal? WeightBrutto { get; set; }

    public bool? IsValid { get; set; }

    public virtual ICollection<WbProductCard> ProductNms { get; set; } = new List<WbProductCard>();
}
