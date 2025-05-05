namespace Shared;

public class PricePushResult
{
    /// <summary>Сколько цен отправлено в Wildberries (количество записей в payload).</summary>
    public int UploadedCount { get; set; }

    /// <summary>Список ошибок расчёта: для какого nmId и почему не получилось посчитать цену.</summary>
    public List<PriceCalculationError> CalculationErrors { get; set; } = new();
}

public class PriceCalculationError
{
    public long NmId { get; set; }
    public string ErrorMessage { get; set; } = "";
}
