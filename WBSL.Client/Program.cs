using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using WBSL.Client.Data.Handlers;
using WBSL.Client.Data.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddMudServices();

builder.Services.AddScoped<AuthHandler>();
builder.Services.AddScoped<ProductMappingService>();
builder.Services.AddScoped<WbProductService>();

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
        BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
        Timeout = TimeSpan.FromMinutes(10)
    };
});
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);


await builder.Build().RunAsync();
