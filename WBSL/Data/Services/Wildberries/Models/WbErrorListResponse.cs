namespace WBSL.Data.Services.Wildberries.Models;

public class WbErrorListResponse
{
    public List<WbErrorItem>? Data { get; set; }
    public bool Error { get; set; }
    public string ErrorText { get; set; }
    public object AdditionalErrors { get; set; }
}
