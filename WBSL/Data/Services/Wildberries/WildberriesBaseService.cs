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
    
    protected Task<HttpClient> GetWbClientAsync(int? accountId = null) 
        => _clientFactory.CreateClientAsync(ExternalAccountType.Wildberries, accountId);
}