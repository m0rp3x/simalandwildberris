using System;
using System.Collections.Generic;

namespace WBSL.Models;

public partial class WbDimension
{
    public long WbProductCardNmID { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public int? Length { get; set; }

    public double? WeightBrutto { get; set; }

    public bool? IsValid { get; set; }

    public virtual WbProductCard WbProductCardNm { get; set; } = null!;
}
