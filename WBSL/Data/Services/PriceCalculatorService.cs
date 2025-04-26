using Microsoft.EntityFrameworkCore;
using WBSL.Data;
using WBSL.Data.Services.Wildberries;

public class PriceCalculatorService
{
    private const decimal HandlingCost = 10m; // Обработка товара на складе
    private const decimal PackagingCost = 10m; // Упаковка
    private const decimal SalaryCost = 10m; // Зарплата
    private const decimal MarginPercent = 20m; // Маржа
    private const decimal RedemptionLossPercent = 3m; // Невыкупы
    private const decimal PlannedDiscountPercent = 60m; // Скидка планируемая

    private readonly CommissionService _сommissionService;
    private readonly BoxTariffService _boxTariffService;
    private readonly QPlannerDbContext _db;

    public PriceCalculatorService(QPlannerDbContext db, CommissionService сommissionService,
        BoxTariffService boxtariffService){
        _сommissionService = сommissionService;
        _boxTariffService = boxtariffService;
        _db = db;
    }

    public async Task<decimal> CalculatePriceAsync(long nmId, int accountId){
        var productCard = await _db.WbProductCards.FirstOrDefaultAsync(p => p.NmID == nmId);

        if (productCard == null)
            throw new Exception($"Product with NMID {nmId} not found.");

        long sid = long.Parse(productCard?.VendorCode ?? "0");
        if (sid == 0){
            throw new Exception($"Product with NMID {nmId} failed to get SID.");
        }

        var product = await _db.products.FirstOrDefaultAsync(p => p.sid == sid);

        if (product == null)
            throw new Exception($"Product with SID {nmId} not found.");

        var purchasePrice = product.price ?? 0m; // Оптовая цена

        var dimensions = new{ product.box_width, product.box_depth, product.box_height };
        
        var (basePrice1, literPrice) = await _boxTariffService.GetCurrentBoxTariffsAsync(accountId);
        var logisticsCost = CalculateLogisticsCost(dimensions.box_width.Value, dimensions.box_depth.Value, dimensions.box_height.Value, basePrice1, literPrice);
        var commissionPercent = await _сommissionService.GetCommissionPercentAsync(productCard.SubjectID.Value, accountId);

        var totalFixedCosts = HandlingCost + PackagingCost + SalaryCost + logisticsCost;

        var totalCommissionPercent = PlannedDiscountPercent + RedemptionLossPercent + commissionPercent;

        var basePrice = purchasePrice + (purchasePrice * MarginPercent / 100m) + totalFixedCosts;

        var finalPrice = basePrice / ((100m - totalCommissionPercent) / 100m);

        return Math.Round(finalPrice.Value, 0); // Округляем до целого рубля
    }

    public Task<int> CalculateDiscountAsync(long nmId){
        // Всегда 60%, как указано в таблице
        return Task.FromResult((int)PlannedDiscountPercent);
    }

    private decimal CalculateLogisticsCost(decimal length, decimal width, decimal height, decimal basePrice, decimal literPrice)
    {
        var volumeCm3 = length * width * height;
        var volumeLiters = volumeCm3 / 1000m;

        if (volumeLiters <= 1)
        {
            return volumeLiters * basePrice;
        }
        else
        {
            return basePrice + (volumeLiters - 1) * literPrice;
        }
    }
}

public class CommissionDto
{
    public decimal percent{ get; set; }
}