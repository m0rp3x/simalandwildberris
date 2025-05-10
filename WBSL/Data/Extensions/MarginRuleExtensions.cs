using WBSL.Data.Models;

namespace WBSL.Data.Extensions;

public static class MarginRuleExtensions
{
    /// <summary>
    /// Находит в коллекции первое правило, в которое попадает цена:
    /// price ∈ [PriceFrom, PriceTo), и возвращает RatePct.
    /// Если ни одно не подошло, бросает или возвращает null.
    /// </summary>
    public static decimal GetRateForPrice(
        this IEnumerable<MarginRule> rules,
        decimal price)
    {
        // Линейный поиск. Можно оптимизировать двоичным, если список очень большой.
        var rule = rules.FirstOrDefault(r => price >= r.PriceFrom
                                             && price <  r.PriceTo);
        if (rule == null)
            throw new KeyNotFoundException(
                $"Не найдено правило для цены {price}");

        return rule.RatePct;
    }
}
