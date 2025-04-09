using System.Net.Http.Headers;
using System.Security.Claims;
using WBSL.Data.Enums;
using WBSL.Data.Services;

namespace WBSL.Data.HttpClientFactoryExt;

public class PlatformHttpClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AccountTokenService _accountService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PlatformHttpClientFactory(
        IHttpClientFactory httpClientFactory,
        AccountTokenService accountService,
        IHttpContextAccessor httpContextAccessor)
    {
        _httpClientFactory = httpClientFactory;
        _accountService = accountService;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<HttpClient> CreateClientAsync(ExternalAccountType platform, int? accountId = null)
    {
        Guid userId = Guid.Empty;
        var user = _httpContextAccessor.HttpContext?.User;
        if (user != null){
            userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty);
        }

        var account = await _accountService.GetAccountAsync(platform, accountId: accountId, userId: userId);
        
        var client = _httpClientFactory.CreateClient(platform.ToString());
        if (platform == ExternalAccountType.WildBerries){
            client.DefaultRequestHeaders.Add("Authorization", account.token);
        }
        if(platform == ExternalAccountType.SimaLand){
            client.DefaultRequestHeaders.Add("X-Api-Key", account.token);
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.token);
        
        return client;
    }
}