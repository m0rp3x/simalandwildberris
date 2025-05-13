namespace Shared;

public class WbCreateBatchError
{
    public int BatchIndex{ get; set; }
    public List<string> VendorCodes{ get; set; } = new();
    public string ErrorText{ get; set; } = "";
    public Dictionary<string, List<string>> AdditionalErrors { get; set; } = 
        new Dictionary<string, List<string>>();
    
    public string FlattenedErrors =>
        string.Join(", ",
            AdditionalErrors
                .SelectMany(kvp => kvp.Value)
                .Distinct()
        );
}

public class WbCreateApiExtendedResult
{
    public WbApiResult Result{ get; set; } = new();
    public int SuccessfulCount{ get; set; }
    public List<string> SuccessfulVendorCodes{ get; set; } = new();
    public List<WbCreateBatchError> BatchErrors{ get; set; } = new();
}