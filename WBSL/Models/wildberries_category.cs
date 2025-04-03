using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WBSL.Models;

public partial class wildberries_category
{
    public int id { get; set; }

    public int parent_id { get; set; }

    public string name { get; set; } = null!;

    public string? parent_name { get; set; }
    [JsonIgnore]

    public virtual wildberries_parrent_category parent { get; set; } = null!;
}
