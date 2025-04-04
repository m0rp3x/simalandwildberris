namespace WBSL.Data.Handlers;

public class HttpClientNameHandler : DelegatingHandler
{
    private readonly string _clientName;

    public HttpClientNameHandler(string clientName)
    {
        _clientName = clientName;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Options.Set(new HttpRequestOptionsKey<string>("HttpClientName"), _clientName);
        return base.SendAsync(request, cancellationToken);
    }
}