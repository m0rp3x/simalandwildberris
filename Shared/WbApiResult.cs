namespace Shared;

public class WbApiResult
{
    public bool Error { get; set; }
    public string ErrorText{ get; set; } = string.Empty;
    public Dictionary<string, List<string>>? AdditionalErrors { get; set; }
    
    public int? TotalCount   { get; set; }
    public int? SuccessCount { get; set; }
    public int? ErrorCount   { get; set; }
}
public class WbApiExtendedResult
{
    public WbApiResult Result { get; set; } = new();
    public int SuccessfulCount { get; set; }
    public List<string> SuccessfulVendorCodes { get; set; } = new();
}