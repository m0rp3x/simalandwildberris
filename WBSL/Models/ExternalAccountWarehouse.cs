namespace WBSL.Models;

public class ExternalAccountWarehouse
{
    public int Id{ get; set; }
    public int ExternalAccountId { get; set; }
    public external_account ExternalAccount { get; set; } = null!;
    public int WarehouseId { get; set; }
}