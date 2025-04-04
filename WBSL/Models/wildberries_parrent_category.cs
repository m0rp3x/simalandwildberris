using System;
using System.Collections.Generic;

namespace WBSL.Models;

public partial class wildberries_parrent_category
{
    public int id { get; set; }

    public string name { get; set; } = null!;

    public virtual ICollection<wildberries_category> wildberries_categories { get; set; } = new List<wildberries_category>();
}
