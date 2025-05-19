namespace WBSL.Models;

public class ExternalAccountWarehouse
{
    public int ExternalAccountId { get; set; }
    public external_account ExternalAccount { get; set; } = null!;
    public int WarehouseId { get; set; }
}