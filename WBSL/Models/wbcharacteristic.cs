using System;
using System.Collections.Generic;

namespace WBSL.Models;

public partial class WbCharacteristic
{
    public int Id { get; set; }

    public long WbProductCardNmID { get; set; }

    public string? Name { get; set; }

    public string? Value { get; set; }

    public virtual WbProductCard WbProductCardNm { get; set; } = null!;
}
