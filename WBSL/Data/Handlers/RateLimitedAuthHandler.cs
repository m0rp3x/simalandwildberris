using WBSL.Data.Enums;
using WBSL.Data.Services;

namespace WBSL.Data.Handlers;

public class RateLimitedAuthHandler : DelegatingHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private static readonly RollingWindowRateLimiter _limiter = 
        new(TimeSpan.FromMinutes(1), 100);
    public RateLimitedAuthHandler(IServiceProvider serviceProvider, IHttpContextAccessor httpContextAccessor){
        _serviceProvider = serviceProvider;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken){
        await _limiter.WaitAsync(cancellationToken);

        try{
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
    
    public class RollingWindowRateLimiter
    {
        private readonly Queue<DateTime> _requestTimes;
        private readonly TimeSpan _window;
        private readonly int _maxRequests;
        private readonly object _lock = new();

        public RollingWindowRateLimiter(TimeSpan window, int maxRequests)
        {
            _window = window;
            _maxRequests = maxRequests;
            _requestTimes = new Queue<DateTime>(maxRequests);
        }

        public async Task WaitAsync(CancellationToken ct)
        {
            while (true)
            {
                lock (_lock)
                {
                    var now = DateTime.UtcNow;
                    while (_requestTimes.Count > 0 && now - _requestTimes.Peek() > _window)
                        _requestTimes.Dequeue();

                    if (_requestTimes.Count < _maxRequests)
                    {
                        _requestTimes.Enqueue(now);
                        return;
                    }
                }

                await Task.Delay(100, ct).ConfigureAwait(false);
            }
        }

        public void Release()
        {
            lock (_lock)
            {
                if (_requestTimes.Count > 0)
                    _requestTimes.Dequeue();
            }
        }
    }
}