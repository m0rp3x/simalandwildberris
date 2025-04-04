using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using WBSL.Client.Data.Handlers;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddMudServices();

builder.Services.AddScoped<AuthHandler>();

builder.Services.AddScoped(sp =>
{
    var jsRuntime = sp.GetRequiredService<IJSRuntime>();
    var snackbar = sp.GetRequiredService<ISnackbar>();

    var handlerChain = new SnackbarHttpHandler(snackbar)
    {
        InnerHandler = new AuthHandler(jsRuntime)
        {
            InnerHandler = new HttpClientHandler()
        }
    };

    return new HttpClient(handlerChain)
    {
        BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
    };
});


await builder.Build().RunAsync();
