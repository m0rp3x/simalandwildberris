namespace Shared;

public class WbApiResult
{
    public bool Error { get; set; }
    public string ErrorText{ get; set; } = string.Empty;
    public Dictionary<string, List<string>>? AdditionalErrors { get; set; }
}
