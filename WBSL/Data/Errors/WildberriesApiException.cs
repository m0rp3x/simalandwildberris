namespace WBSL.Data.Errors;

public class WildberriesApiException : Exception
{
    public int StatusCode { get; }
    public string ResponseContent { get; }

    public WildberriesApiException(string message, int statusCode, string responseContent)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseContent = responseContent;
    }
}
