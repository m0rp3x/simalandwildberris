public class ProductNameUpdateDto
{
    public long Sid { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? QtyMultiplier { get; set; } // 👈 добавлено
}