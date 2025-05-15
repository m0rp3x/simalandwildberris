namespace Shared;

public class WarehouseUpdateResult
{
    /// <summary>Идентификатор склада.</summary>
    public int WarehouseId { get; set; }

    /// <summary>Успешно обновлённые продукты.</summary>
    public List<ProductInfo> Successful { get; set; } = new();

    /// <summary>Сколько всего товаров попытались обновить (успешных + неуспешных).</summary>
    public int ProcessedCount { get; set; }

    /// <summary>Неуспешные попытки с ошибками.</summary>
    public List<FailedStock> Failed { get; set; } = new();
}

public class FailedStock
{
    public string Sku{ get; set; }
    public int Amount{ get; set; }

    // код и сообщение с WB
    public string ErrorCode{ get; set; }
    public string ErrorMessage{ get; set; }
}