using Shared.Enums;
using WBSL.Data.Enums;
using WBSL.Data.HttpClientFactoryExt;

namespace WBSL.Data.Services.Wildberries;

public abstract class WildberriesBaseService
{
    protected PlatformHttpClientFactory _clientFactory { get; }
    
    protected WildberriesBaseService(PlatformHttpClientFactory factory){
        _clientFactory = factory;
    }
    
    protected virtual Task<HttpClient> GetWbClientAsync(int accountId, bool isSync = false) 
        => _clientFactory.CreateClientAsync(ExternalAccountType.Wildberries, accountId, isSync);
}