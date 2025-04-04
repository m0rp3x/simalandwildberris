namespace WBSL.Data.Services.Wildberries;

public abstract class WildberriesBaseService
{
    protected HttpClient WbClient { get; }
    
    protected WildberriesBaseService(IHttpClientFactory factory)
    {
        WbClient = factory.CreateClient("WildBerries");
    }
}