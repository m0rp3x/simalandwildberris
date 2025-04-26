namespace WBSL.Data.Models;

public class BalanceUpdateRule
{
    public int Id { get; set; } // Обязательно первичный ключ
    public int FromStock { get; set; }  // Сколько штук от
    public int ToStock { get; set; }    // Сколько штук до
    public TimeSpan UpdateInterval { get; set; } // Интервал обновления
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // Когда правило создано
}