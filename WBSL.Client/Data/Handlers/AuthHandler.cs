using System.Net.Http.Headers;
using Microsoft.JSInterop;

namespace WBSL.Client.Data.Handlers;

public class AuthHandler : DelegatingHandler
{
    private readonly IJSRuntime _jsRuntime;

    public AuthHandler(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", cancellationToken, "authToken");

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
