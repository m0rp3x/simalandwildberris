using System.Security.Claims;
using WBSL.Data.Enums;
using WBSL.Data.Services;

namespace WBSL.Data.Handlers;

public class RateLimitedAuthHandler : DelegatingHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private static int _requestCount = 0;
    private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(100, 100); // Макс 100 запросов
    private static Timer _resetTimer;

    static RateLimitedAuthHandler(){
        // Сбрасываем счётчик каждую минуту
        _resetTimer = new Timer(_ => {
            Interlocked.Exchange(ref _requestCount, 0); // Атомарно сбрасываем
            _semaphore.Release(100 - _requestCount); // Освобождаем семафор
        }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public RateLimitedAuthHandler(IServiceProvider serviceProvider, IHttpContextAccessor httpContextAccessor){
        _serviceProvider = serviceProvider;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken){
        await _semaphore.WaitAsync(cancellationToken);

        try{
            Interlocked.Increment(ref _requestCount);

            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null)
                throw new InvalidOperationException("No user context available.");

            using (var scope = _serviceProvider.CreateScope()){
                var accountRepo = scope.ServiceProvider.GetRequiredService<AccountTokenService>();
                var account =
                    await accountRepo.GetCurrentUserExternalAccountAsync(user, ExternalAccountType.WildBerris);

                request.Headers.TryAddWithoutValidation("Authorization", account!.token);
            }

            return await base.SendAsync(request, cancellationToken);
        }
        finally{
        }
    }
}