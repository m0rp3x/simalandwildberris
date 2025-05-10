using Microsoft.EntityFrameworkCore;
using Shared;
using WBSL.Data;
using WBSL.Data.Services.Wildberries;
using WBSL.Models;

public class PriceCalculatorService
{
    private readonly CommissionService _commissionService;
    private readonly BoxTariffService _boxTariffService;
    private readonly QPlannerDbContext _db;
    private PriceCalculatorSettingsDto _settings;

    public PriceCalculatorService(QPlannerDbContext db, CommissionService commissionService,
        BoxTariffService boxTariffService)
    {
        _commissionService = commissionService;
        _boxTariffService = boxTariffService;
        _db = db;
        _settings = new PriceCalculatorSettingsDto();
    }
    
    private List<WbProductCard> _productCards;
    private List<product> _products;
    private (decimal BasePrice1, decimal LiterPrice) _boxTariffs;
    private Dictionary<int, decimal?> _commissionPercents;
    
    public async Task<List<long>> PrepareCalculationDataAsync(int accountId, int categoryId)
    {
        _productCards = await _db.WbProductCards
            .AsNoTracking()
            .Where(x => x.externalaccount_id == accountId && x.SubjectID == categoryId)
            .ToListAsync();

        var sids = _productCards
            .Where(pc => long.TryParse(pc.VendorCode, out _))
            .Select(pc => long.Parse(pc.VendorCode))
            .Distinct()
            .ToList();

        _products = await _db.products
            .AsNoTracking()
            .Where(p => sids.Contains(p.sid))
            .ToListAsync();
        if (_products.Count == 0){
            throw new Exception("Products not found in database");
        }

        _boxTariffs = await _boxTariffService.GetCurrentBoxTariffsAsync(accountId);

        var subjectIds = _productCards
            .Where(pc => pc.SubjectID.HasValue)
            .Select(pc => pc.SubjectID.Value)
            .Distinct()
            .ToList();

        _commissionPercents = new Dictionary<int, decimal?>();
        foreach (var subjectId in subjectIds)
        {
            _commissionPercents[subjectId] = await _commissionService.GetCommissionPercentAsync(subjectId, accountId);
        }
        var matchedVendorCodes = _products
            .Select(p => p.sid.ToString())
            .ToHashSet(); // Быстрая проверка

        var matchedNmIds = _productCards
            .Where(pc => pc.VendorCode != null && matchedVendorCodes.Contains(pc.VendorCode))
            .Select(pc => pc.NmID)
            .ToList();

        return matchedNmIds;
    }
    public Task<decimal> CalculatePriceAsync(long nmId, PriceCalculatorSettingsDto settingsDto, int accountId)
    {
        var productCard = _productCards.FirstOrDefault(p => p.NmID == nmId);

        if (productCard == null)
            return Task.FromResult(0m);

        long sid = long.Parse(productCard?.VendorCode ?? "0");
        if (sid == 0)
            return Task.FromResult(0m);

        var product = _products.FirstOrDefault(p => p.sid == sid);
        if (product == null)
            return Task.FromResult(0m);

        var purchasePrice = product.wholesale_price ?? 0m;
        
        var effectivePurchasePrice = settingsDto.IsMinimal
            ? purchasePrice * product.qty_multiplier
            : purchasePrice;
        
        var dimensions = new { product.box_width, product.box_depth, product.box_height };

        var (basePrice1, literPrice) = _boxTariffs;

        var logisticsCost = CalculateLogisticsCost(
            dimensions.box_width.Value,
            dimensions.box_depth.Value,
            dimensions.box_height.Value,
            basePrice1, literPrice);

        var commissionPercent = productCard.SubjectID.HasValue && _commissionPercents.TryGetValue(productCard.SubjectID.Value, out var com)
            ? com
            : 0m;

        var totalFixedCosts = settingsDto.HandlingCost + settingsDto.PackagingCost + settingsDto.SalaryCost + logisticsCost;
        var totalCommissionPercent = settingsDto.RedemptionLossPercent + commissionPercent;
        decimal? commissionDenominator = (100m - totalCommissionPercent) / 100m;
        decimal? discountDenominator = (100m - settingsDto.AddedDiscountPercent) / 100m;
        if (commissionDenominator <= 0 || discountDenominator <= 0)
        {
            throw new InvalidOperationException(
                $"Сумма всех % ({totalCommissionPercent:F2}%) должна быть меньше 100%."
            );
        }
        var basePrice = effectivePurchasePrice + (effectivePurchasePrice * settingsDto.MarginPercent / 100m) + totalFixedCosts;
        var finalPrice = basePrice / commissionDenominator / discountDenominator;

        return Task.FromResult(Math.Round((decimal)finalPrice, 1));
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