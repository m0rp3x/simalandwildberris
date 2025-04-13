namespace WBSL.Data.Services.Wildberries.Models;

public class WbErrorItem
{
    public List<string> Errors { get; set; }
    public string VendorCode { get; set; }
    public string Object { get; set; }
    public int ObjectID { get; set; }
    public DateTime UpdateAt { get; set; }
}