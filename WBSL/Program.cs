using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using WBSL.Components;
using Microsoft.IdentityModel.Tokens;
using WBSL.Data;
using System.Text;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using WBSL.Data.Config;
using WBSL.Data.Handlers;
using WBSL.Data.Hangfire;
using WBSL.Data.HttpClientFactoryExt;
using WBSL.Data.Services;
using WBSL.Data.Services.Wildberries;

var builder = WebApplication.CreateBuilder(args);

// Add MudBlazor services
builder.Services.AddMudServices();
builder.Services.AddDbContext<QPlannerDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
// Hangfire
builder.Services.AddHangfireWithJobs(builder.Configuration);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();
// JWT config
var jwtKey = builder.Configuration["Jwt:Key"];
var key = Encoding.ASCII.GetBytes(jwtKey);

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
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

builder.Services.AddScoped<PlatformHttpClientFactory>(sp => 
    new PlatformHttpClientFactory(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<AccountTokenService>(),
        sp.GetRequiredService<IHttpContextAccessor>()));


builder.Services.AddTransient<RateLimitedAuthHandler>();
builder.Services.AddTransient<AccountTokenService>();
builder.Services.AddScoped<WildberriesService>();
builder.Services.AddScoped<WildberriesCategoryService>();
builder.Services.AddScoped<WildberriesProductsService>();

builder.Services.AddHttpClient("SimaLand", client => {
        client.BaseAddress = new Uri("https://www.sima-land.ru/api/v3/");
    })
    .AddHttpMessageHandler(sp => new HttpClientNameHandler("SimaLand"))
    .AddHttpMessageHandler(sp => new RateLimitedAuthHandler(sp.GetRequiredService<IOptions<RateLimitConfig>>()));


builder.Services.AddHttpClient("WildBerries", client =>
    {
        client.BaseAddress = new Uri("https://content-api.wildberries.ru");
    })
    .AddHttpMessageHandler(sp => new HttpClientNameHandler("WildBerries"))
    .AddHttpMessageHandler(sp => new RateLimitedAuthHandler(sp.GetRequiredService<IOptions<RateLimitConfig>>()));

var app = builder.Build();
app.MapControllers(); // <-- обязательно!

if (app.Environment.IsDevelopment())
{   
    app.UseWebAssemblyDebugging();
    app.UseHangfireDashboard();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

HangfireConfig.RegisterJobs();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(WBSL.Client._Imports).Assembly);

app.Run();
