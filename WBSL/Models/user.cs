using System;
using System.Collections.Generic;

namespace WBSL.Models;

public partial class user
{
    public Guid id { get; set; }

    public string user_name { get; set; } = null!;

    public string? email { get; set; }

    public string password_hash { get; set; } = null!;

    public DateTime? created_at { get; set; }

    public virtual ICollection<external_account> external_accounts { get; set; } = new List<external_account>();
}
