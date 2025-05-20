using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WBSL.Data.Models;

[NotMapped]
public class BalanceUpdateRule
{
    

    public int Id { get; set; }

    public int FromStock { get; set; }

    public int ToStock { get; set; }

    public string? UpdateInterval { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public TimeSpan Jopa;

    public BalanceUpdateRule()
    {
        Jopa = UpdateInterval == null ? TimeSpan.Zero : TimeSpan.FromSeconds(double.Parse(UpdateInterval));
    }
}
