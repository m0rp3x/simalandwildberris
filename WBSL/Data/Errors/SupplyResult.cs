namespace WBSL.Data.Errors;

public class SupplyResult
{
    public bool Success       { get; init; }
    public string? SupplyId   { get; init; }
    public bool DbUpdated      { get; init; }
    public string? ErrorCode  { get; init; }
    public string? Message    { get; init; }
}
