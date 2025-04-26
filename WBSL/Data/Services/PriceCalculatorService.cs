using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;

public class PriceCalculatorService
{
    private readonly HttpClient _httpClient;

    private const decimal HandlingFee = 10m;    // Обработка товара
    private const decimal PackagingFee = 10m;   // Упаковка
    private const decimal SalaryFee = 10m;       // Зарплата
    private const decimal MarginPercent = 20m;  // Маржа
    private const decimal PlannedDiscountPercent = 60m; // Планируемая скидка
    private const decimal NonRedemptionPercent = 3m;     // Невыкупы

    public PriceCalculatorService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<decimal> CalculateFinalPrice(ProductDto product)
    {
        decimal logisticCost = await CalculateLogisticsCost(product.BoxWidth, product.BoxHeight, product.BoxDepth);
        decimal totalFixedCosts = HandlingFee + PackagingFee + SalaryFee + logisticCost;

        decimal marketplaceCommissionPercent = await GetMarketplaceCommission();

        decimal purchasePrice = product.Price;
        decimal markupPrice = purchasePrice + (purchasePrice * MarginPercent / 100);
        decimal priceWithCosts = markupPrice + totalFixedCosts;

        decimal totalCommissionPercent = PlannedDiscountPercent + NonRedemptionPercent + marketplaceCommissionPercent;

        decimal finalPrice = priceWithCosts / ((100 - totalCommissionPercent) / 100);

        return Math.Round(finalPrice, 2);
    }

    private async Task<decimal> CalculateLogisticsCost(decimal widthCm, decimal heightCm, decimal depthCm)
    {
        decimal volumeCm3 = widthCm * heightCm * depthCm;
        decimal volumeLiters = volumeCm3 / 1000m;

        if (volumeLiters <= 1)
        {
            return volumeLiters * 47.5m;
        }
        else
        {
            return 47.5m + ((volumeLiters - 1) * 11.88m);
        }
    }

    private async Task<decimal> GetMarketplaceCommission()
    {
        var response = await _httpClient.GetAsync("https://common-api.wildberries.ru/api/v1/tariffs/commission");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        decimal commissionPercent = json.RootElement.GetProperty("commissionPercent").GetDecimal();

        return commissionPercent;
    }
}

public class ProductDto
{
    public decimal Price { get; set; } // поле price из products
    public decimal BoxWidth { get; set; } // поле box_width из products
    public decimal BoxHeight { get; set; } // поле box_height из products
    public decimal BoxDepth { get; set; } // поле box_depth из products
}
