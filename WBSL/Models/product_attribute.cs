using System;
using System.Collections.Generic;

namespace WBSL.Models;

public partial class product_attribute
{
    public int id { get; set; }

    public long product_sid { get; set; }

    public string attr_name { get; set; } = null!;

    public string? value_text { get; set; }

    public DateTime? created_at { get; set; }

    public virtual product product_s { get; set; } = null!;
}
