using Shared.Enums;
using WBSL.Data.Enums;
using WBSL.Data.HttpClientFactoryExt;

namespace WBSL.Data.Services.Simaland;

public abstract class SimalandBaseService
{
    protected readonly PlatformHttpClientFactory _clientFactory;
    protected SimalandBaseService(PlatformHttpClientFactory factory)
    {
        _clientFactory = factory;
    }
    protected Task<HttpClient> GetClientAsync(int accountId) 
        => _clientFactory.CreateClientAsync(ExternalAccountType.SimaLand, accountId);
}