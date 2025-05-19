using System.ComponentModel.DataAnnotations;

namespace WBSL.Data.Models;

public class JobSchedule
{
    [Key]
    public string JobId    { get; set;  } = null!;
    public string CronExpr { get; set;  } = null!;
    public DateTime LastUpdated { get; set; }
}