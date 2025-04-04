using System;
using System.Collections.Generic;

namespace WBSL.Models;

public partial class WbSku
{
    public int Id { get; set; }

    public long WbSizeChrtID { get; set; }

    public string? Sku { get; set; }

    public virtual WbSize WbSizeChrt { get; set; } = null!;
}
