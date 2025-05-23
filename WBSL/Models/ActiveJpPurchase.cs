namespace WBSL.Models;

public class ActiveJpPurchase
{
    public int Id { get; set; }
    public int JpPurchaseId { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsClosed { get; set; }
}
