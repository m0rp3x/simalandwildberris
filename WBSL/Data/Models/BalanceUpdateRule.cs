using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WBSL.Data.Models;

[NotMapped]
public class BalanceUpdateRule
{
    public int Id{ get; set; } // Обязательно первичный ключ
    public int FromStock{ get; set; } // Сколько штук от
    public int ToStock{ get; set; } // Сколько штук до
    [ScaffoldColumn(false)]
    public TimeSpan UpdateInterval{ get; set; } // Интервал обновления
    public DateTime CreatedAt{ get; set; } = DateTime.UtcNow; // Когда правило создано
    
    [NotMapped]
    [Display(Name = "Интервал обновления (чч:мм:сс)")]
    public string UpdateIntervalString
    {
        get => UpdateInterval.ToString(@"hh\:mm\:ss");
        set => UpdateInterval = TimeSpan.TryParse(value, out var ts) ? ts : TimeSpan.Zero;
    }

}