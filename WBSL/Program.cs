using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using WBSL.Components;
using Microsoft.IdentityModel.Tokens;
using WBSL.Data;
using System.Text;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Shared;
using WBSL.Client.Data.Services;
using WBSL.Data.Config;
using WBSL.Data.Handlers;
using WBSL.Data.Hangfire;
using WBSL.Data.HttpClientFactoryExt;
using WBSL.Data.Services;
using WBSL.Data.Services.Simaland;
using WBSL.Data.Services.Wildberries;
using WBSL.Services;

var builder = WebApplication.CreateBuilder(args);

// Add MudBlazor services
builder.Services.AddMudServices();
builder.Services.AddDbContext<QPlannerDbContext>(options => {
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});
// Hangfire
builder.Services.AddHangfireWithJobs(builder.Configuration);
builder.Services.AddCoreAdmin();
// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();
// JWT config
var jwtKey = builder.Configuration["Jwt:Key"];
var key = Encoding.ASCII.GetBytes(jwtKey);

builder.Services.AddAuthentication(options => {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options => {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters{
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = false,
            ValidateAudience = false
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers();

builder.Services.Configure<RateLimitConfig>("SimaLand",
    builder.Configuration.GetSection("RateLimits:SimaLand"));
builder.Services.Configure<RateLimitConfig>("WildBerries",
    builder.Configuration.GetSection("RateLimits:WildBerries"));
builder.Services.Configure<RateLimitConfig>("WildBerriesMarketPlace",
    builder.Configuration.GetSection("RateLimits:WildBerriesMarketPlace"));
builder.Services.Configure<RateLimitConfig>("WildBerriesDiscountPrices",
    builder.Configuration.GetSection("RateLimits:WildBerriesDiscountPrices"));
builder.Services.Configure<RateLimitConfig>("WildBerriesCommonApi",
    builder.Configuration.GetSection("RateLimits:WildBerriesCommonApi"));

builder.Services.AddScoped<PlatformHttpClientFactory>(sp =>
    new PlatformHttpClientFactory(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<AccountTokenService>(),
        sp.GetRequiredService<IHttpContextAccessor>()));


builder.Services.AddScoped<AccountTokenService>();
builder.Services.AddScoped<WildberriesService>();
builder.Services.AddScoped<WildberriesCategoryService>();
builder.Services.AddScoped<WildberriesProductsService>();
builder.Services.AddScoped<WildberriesPriceService>();
builder.Services.AddScoped<PriceCalculatorService>();
builder.Services.AddScoped<CommissionService>();
builder.Services.AddScoped<BoxTariffService>();
builder.Services.AddScoped<WildberriesCharacteristicsService>();
builder.Services.AddScoped<SimalandFetchService>();
builder.Services.AddScoped<ProductMappingService>();
builder.Services.AddScoped<WbProductService>();
builder.Services.AddScoped<PriceCalculatorService>();
builder.Services.AddScoped<IDbContextFactory<QPlannerDbContext>, ManualDbContextFactory>();
builder.Services.AddScoped<ISimaLandService, SimaLandService>();

builder.Services.AddScoped<SimalandClientService>();
builder.Services.AddHostedService<BalanceUpdateScheduler>();
builder.Services.AddSingleton<PriceCalculatorSettingsDto>(); // Настройки (одни на всё приложение)

builder.Services.AddScoped<PriceCalculatorService>(); // Сам сервис калькулятора цен

builder.Services
    .AddHttpClient("SimaLand", client => { client.BaseAddress = new Uri("https://www.sima-land.ru/api/v3/"); })
    .AddHttpMessageHandler(sp => new HttpClientNameHandler("SimaLand"))
    .AddHttpMessageHandler(sp => {
        var a = sp.GetRequiredService<IOptionsSnapshot<RateLimitConfig>>().Get("SimaLand");
        return new RateLimitedAuthHandler(a, "SimaLand");
    });

builder.Services.AddHttpClient("Wildberries",
        client => { client.BaseAddress = new Uri("https://content-api.wildberries.ru"); })
    .AddHttpMessageHandler(sp => new HttpClientNameHandler("WildBerries"))
    .AddHttpMessageHandler(sp => {
        var a = sp.GetRequiredService<IOptionsSnapshot<RateLimitConfig>>().Get("WildBerries");
        return new RateLimitedAuthHandler(a, "WildBerries");
    });

builder.Services.AddHttpClient("WildBerriesMarketPlace",
        client => { client.BaseAddress = new Uri("https://marketplace-api.wildberries.ru"); })
    .AddHttpMessageHandler(sp => new HttpClientNameHandler("WildBerriesMarketPlace"))
    .AddHttpMessageHandler(sp => {
        var a = sp.GetRequiredService<IOptionsSnapshot<RateLimitConfig>>().Get("WildBerriesMarketPlace");
        return new RateLimitedAuthHandler(a, "WildBerriesMarketPlace");
    });

builder.Services.AddHttpClient("WildBerriesDiscountPrices",
        client => { client.BaseAddress = new Uri("https://discounts-prices-api.wildberries.ru"); })
    .AddHttpMessageHandler(sp => new HttpClientNameHandler("WildBerriesDiscountPrices"))
    .AddHttpMessageHandler(sp => {
        var a = sp.GetRequiredService<IOptionsSnapshot<RateLimitConfig>>().Get("WildBerriesDiscountPrices");
        return new RateLimitedAuthHandler(a, "WildBerriesDiscountPrices");
    });

builder.Services.AddHttpClient("WildBerriesCommonApi",
        client => { client.BaseAddress = new Uri("https://common-api.wildberries.ru/"); })
    .AddHttpMessageHandler(sp => new HttpClientNameHandler("WildBerriesCommonApi"))
    .AddHttpMessageHandler(sp => {
        var a = sp.GetRequiredService<IOptionsSnapshot<RateLimitConfig>>().Get("WildBerriesCommonApi");
        return new RateLimitedAuthHandler(a, "WildBerriesCommonApi");
    });

builder.Services.Remove(
    builder.Services.FirstOrDefault(d =>
        d.ServiceType == typeof(Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionMiddleware))!);

var app = builder.Build();
app.MapControllers(); // <-- обязательно!
app.UseHangfireDashboard();

app.UseStaticFiles();
app.MapDefaultControllerRoute();
app.UseCoreAdminCustomAuth(_ => Task.FromResult(true));
if (app.Environment.IsDevelopment()){
    app.UseWebAssemblyDebugging();
}
else{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}


HangfireConfig.RegisterJobs();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(WBSL.Client._Imports).Assembly);

app.Run();