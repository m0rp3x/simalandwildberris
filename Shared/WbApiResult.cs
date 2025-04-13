namespace Shared;

public class WbApiResult
{
    public bool Error { get; set; }
    public string ErrorText { get; set; }
    public object AdditionalErrors { get; set; }
}
