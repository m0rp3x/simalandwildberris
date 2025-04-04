using System;
using System.Collections.Generic;

namespace WBSL.Models;

public partial class WbPhoto
{
    public int Id { get; set; }

    public long WbProductCardNmID { get; set; }

    public string? Big { get; set; }

    public string? C246x328 { get; set; }

    public string? C516x688 { get; set; }

    public string? Hq { get; set; }

    public string? Square { get; set; }

    public string? Tm { get; set; }

    public virtual WbProductCard WbProductCardNm { get; set; } = null!;
}
