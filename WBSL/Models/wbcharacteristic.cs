using System;
using System.Collections.Generic;

namespace WBSL.Models;

public partial class WbCharacteristic
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Value { get; set; }

    public virtual ICollection<product> product_s { get; set; } = new List<product>();
}
