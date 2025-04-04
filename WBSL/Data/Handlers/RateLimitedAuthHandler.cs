using System.Collections.Concurrent;
using WBSL.Data.Enums;
using WBSL.Data.Services;

namespace WBSL.Data.Handlers;

public class RateLimitedAuthHandler : DelegatingHandler
{
    private readonly IServiceProvider _serviceProvider;
    private static readonly ConcurrentDictionary<string, RollingWindowRateLimiter> _limiters = new();

    public RateLimitedAuthHandler(IServiceProvider serviceProvider){
        _serviceProvider = serviceProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken){
        if (!request.Options.TryGetValue(new HttpRequestOptionsKey<string>("HttpClientName"), out var clientName)){
            clientName = "default";
        }

        // Получаем или создаем лимитер для этого клиента
        var limiter = _limiters.GetOrAdd(clientName, _ => new RollingWindowRateLimiter(TimeSpan.FromMinutes(1), 80));

        // Ждем разрешения на запрос
        await limiter.WaitAsync(cancellationToken);

        try{
            using var scope = _serviceProvider.CreateScope();
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();

            var user = httpContextAccessor.HttpContext?.User;
            if (user == null)
                throw new InvalidOperationException("No user context available.");

            var accountRepo = scope.ServiceProvider.GetRequiredService<AccountTokenService>();
            var account = await accountRepo.GetCurrentUserExternalAccountAsync(user, ExternalAccountType.WildBerris);

            request.Headers.Remove("Authorization");
            request.Headers.Add("Authorization", account!.token);

            return await base.SendAsync(request, cancellationToken);
        }
        finally{
            // Освобождение не требуется, так как скользящее окно само по себе регулирует лимит
        }
    }

    public class RollingWindowRateLimiter
    {
        private readonly Queue<DateTime> _requestTimes = new();
        private readonly TimeSpan _window;
        private readonly int _maxRequests;
        private readonly SemaphoreSlim _semaphore = new(1, 1); // Блокировка для потокобезопасности

        public RollingWindowRateLimiter(TimeSpan window, int maxRequests){
            _window = window;
            _maxRequests = maxRequests;
        }

        public async Task WaitAsync(CancellationToken ct){
            while (true){
                await _semaphore.WaitAsync(ct);
                try{
                    var now = DateTime.UtcNow;

                    // Удаляем старые запросы
                    while (_requestTimes.Count > 0 && now - _requestTimes.Peek() > _window)
                        _requestTimes.Dequeue();

                    if (_requestTimes.Count < _maxRequests){
                        _requestTimes.Enqueue(now);
                        return;
                    }
                }
                finally{
                    _semaphore.Release();
                }

                await Task.Delay(500, ct); // Ждём перед повторной попыткой
            }
        }
    }
}