namespace Shared;

public class PriceCalculatorSettingsDto
{
    public decimal HandlingCost { get; set; } = 10m; // Стоимость обработки
    public decimal PackagingCost { get; set; } = 10m; // Стоимость упаковки
    public decimal SalaryCost { get; set; } = 10m; // Стоимость зарплаты
    public decimal MarginPercent { get; set; } = 20m; // Процент маржи
    public decimal RedemptionLossPercent { get; set; } = 3m; // Процент невыкупа
    public decimal PlannedDiscountPercent { get; set; } = 60m; // Планируемая скидка
}
