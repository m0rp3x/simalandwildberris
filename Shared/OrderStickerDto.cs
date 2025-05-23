namespace Shared;

public class OrderStickerDto
{
    /// <summary>Идентификатор заказа</summary>
    public long OrderId { get; set; }

    /// <summary>Содержимое файла-стикера в Base64</summary>
    public string Base64File { get; set; }

    /// <summary>Тип файла (png, jpg и т.п.)</summary>
    public string FileType { get; set; }

    /// <summary>Ширина стикера в пикселях</summary>
    public int Width { get; set; }

    /// <summary>Высота стикера в пикселях</summary>
    public int Height { get; set; }
}