using System.ComponentModel.DataAnnotations;

namespace Shared;

public class PriceCalculatorSettingsDto
{
    [Required]
    public decimal HandlingCost { get; set; } = 10m; // Стоимость обработки
    [Required]
    public decimal PackagingCost { get; set; } = 10m; // Стоимость упаковки
    [Required]
    public decimal SalaryCost { get; set; } = 10m; // Стоимость зарплаты

    [Required]
    public decimal RedemptionLossPercent { get; set; } = 3m; // Процент невыкупа
    [Required]
    public decimal PlannedDiscountPercent { get; set; } = 60m; 
    [Required]
    public decimal AddedDiscountPercent { get; set; } = 10m; // Добавленная скидка
    [Required]
    public bool IsMinimal { get; set; } = false;
    
    public int? WildberriesCategoryId { get; set; } = 0;
}
