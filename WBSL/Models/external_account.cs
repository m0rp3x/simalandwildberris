using System;
using System.Collections.Generic;

namespace WBSL.Models;

public partial class external_account
{
    public int id { get; set; }

    public Guid user_id { get; set; }

    public string platform { get; set; } = null!;

    public string token { get; set; } = null!;

    public string? name { get; set; }

    public DateTime? added_at { get; set; }

    public int? warehouseid { get; set; }

    public virtual ICollection<WbProductCard> WbProductCards { get; set; } = new List<WbProductCard>();

    public virtual user user { get; set; } = null!;
}
