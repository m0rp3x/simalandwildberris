namespace WBSL.Data.Models;

public class JobSchedule
{
    public string JobId    { get; set;  } = null!;
    public string CronExpr { get; set;  } = null!;
    public DateTime LastUpdated { get; set; }
}