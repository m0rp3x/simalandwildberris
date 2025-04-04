using System;
using System.Collections.Generic;

namespace WBSL.Models;

public partial class WbCursor
{
    public int Id { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public long? NmID { get; set; }

    public int? Total { get; set; }
}
