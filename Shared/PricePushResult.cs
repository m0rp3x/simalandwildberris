namespace Shared;

public class PricePushResult
{
    /// <summary>Общее число цен, которые мы пытались запушить</summary>
    public int TotalCount { get; set; }

    /// <summary>Сколько цен загрузилось успешно</summary>
    public int SuccessCount { get; set; }

    /// <summary>Сколько цен упало с ошибкой</summary>
    public int FailedCount { get; set; }

    /// <summary>Подробная информация по каждой неудачной пачке</summary>
    public List<BatchError> Errors { get; set; } = new();
}

public class BatchError
{
    /// <summary>Индекс пачки (0-based)</summary>
    public int BatchIndex { get; set; }

    /// <summary>Размер этой пачки (<=100)</summary>
    public int BatchSize { get; set; }

    /// <summary>HTTP-код ответа или –1 при исключении на клиенте</summary>
    public int StatusCode { get; set; }

    /// <summary>Текст ошибки из тела ответа или текст исключения</summary>
    public string ErrorText { get; set; } = "";
}