public class ProductNameUpdateDto
{
    public long Sid { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? QtyMultiplier { get; set; }
    public string? UnitName { get; set; } // 👈 добавлено
    public string? Description { get; set; } // 👈 добавлено
}