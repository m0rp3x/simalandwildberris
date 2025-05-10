namespace WBSL.Data.Models;

public class MarginRule
{
    public int    Id        { get; set; }
    public decimal PriceFrom { get; set; }
    public decimal PriceTo   { get; set; }
    public decimal RatePct   { get; set; }
}
