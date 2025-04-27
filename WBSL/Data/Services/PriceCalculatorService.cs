using Microsoft.EntityFrameworkCore;
using Shared;
using WBSL.Data;
using WBSL.Data.Services.Wildberries;

public class PriceCalculatorService
{
    private readonly CommissionService _commissionService;
    private readonly BoxTariffService _boxTariffService;
    private readonly QPlannerDbContext _db;
    private readonly PriceCalculatorSettingsDto _settings;

    public PriceCalculatorService(QPlannerDbContext db, CommissionService commissionService,
        BoxTariffService boxTariffService, PriceCalculatorSettingsDto settings)
    {
        _commissionService = commissionService;
        _boxTariffService = boxTariffService;
        _db = db;
        _settings = settings;
    }

    public async Task<decimal> CalculatePriceAsync(long nmId, int accountId)
    {
        var productCard = await _db.WbProductCards.FirstOrDefaultAsync(p => p.NmID == nmId);

        if (productCard == null)
            throw new Exception($"Product with NMID {nmId} not found.");

        long sid = long.Parse(productCard?.VendorCode ?? "0");
        if (sid == 0)
        {
            throw new Exception($"Product with NMID {nmId} failed to get SID.");
        }

        var product = await _db.products.FirstOrDefaultAsync(p => p.sid == sid);

        if (product == null)
            throw new Exception($"Product with SID {nmId} not found.");

        var purchasePrice = product.price ?? 0m; // Оптовая цена

        var dimensions = new { product.box_width, product.box_depth, product.box_height };

        var (basePrice1, literPrice) = await _boxTariffService.GetCurrentBoxTariffsAsync(accountId);
        var logisticsCost = CalculateLogisticsCost(dimensions.box_width.Value, dimensions.box_depth.Value, dimensions.box_height.Value, basePrice1, literPrice);
        var commissionPercent = await _commissionService.GetCommissionPercentAsync(productCard.SubjectID.Value, accountId);

        var totalFixedCosts = _settings.HandlingCost + _settings.PackagingCost + _settings.SalaryCost + logisticsCost;

        var totalCommissionPercent = _settings.PlannedDiscountPercent + _settings.RedemptionLossPercent + commissionPercent;

        var basePrice = purchasePrice + (purchasePrice * _settings.MarginPercent / 100m) + totalFixedCosts;

        var finalPrice = basePrice / ((100m - totalCommissionPercent) / 100m);

        return Math.Round(finalPrice.Value, 0);
    }

    public Task<int> CalculateDiscountAsync(long nmId)
    {
        return Task.FromResult((int)_settings.PlannedDiscountPercent);
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
    public decimal percent { get; set; }
}