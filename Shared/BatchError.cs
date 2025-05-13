namespace Shared;

public class WbCreateBatchError
{
    public int BatchIndex{ get; set; }
    public List<string> VendorCodes{ get; set; } = new();
    public string ErrorText{ get; set; } = "";
}

public class WbCreateApiExtendedResult
{
    public WbApiResult Result{ get; set; } = new();
    public int SuccessfulCount{ get; set; }
    public List<string> SuccessfulVendorCodes{ get; set; } = new();
    public List<WbCreateBatchError> BatchErrors{ get; set; } = new();
}