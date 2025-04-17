namespace WBSL.Data.Exceptions;

public class FetchFailedException : Exception
{
    public FetchFailedException(string message, Exception innerException)
        : base(message, innerException) { }
}
