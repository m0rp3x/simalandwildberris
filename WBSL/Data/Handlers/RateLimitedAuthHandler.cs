using System.Collections.Concurrent;
using WBSL.Data.Enums;
using WBSL.Data.Services;

namespace WBSL.Data.Handlers;

public class RateLimitedAuthHandler : DelegatingHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly int _requestLimit;
    private readonly int _timeRequestLimit;
    private static readonly ConcurrentDictionary<string, RollingWindowRateLimiter> _limiters = new();
    private static readonly ConcurrentDictionary<string, CircuitBreakerState> _circuitBreakers = new();
    
    public RateLimitedAuthHandler(IServiceProvider serviceProvider, int requestLimit, int timeRequestLimit)
    {
        _serviceProvider = serviceProvider;
        _requestLimit = requestLimit;
        _timeRequestLimit = timeRequestLimit;
    }


    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken){
        if (!request.Options.TryGetValue(new HttpRequestOptionsKey<string>("HttpClientName"), out var clientName)){
            clientName = "default";
        }

        var limiter = _limiters.GetOrAdd(clientName, _ => new RollingWindowRateLimiter(TimeSpan.FromSeconds(_timeRequestLimit), _requestLimit));
        var circuitBreaker = GetCircuitBreaker(clientName);
        if (circuitBreaker.IsOpen)
            throw new CircuitBrokenException($"API {clientName} is temporarily unavailable");
        
        try{
            await limiter.WaitAsync(cancellationToken);
            using var scope = _serviceProvider.CreateScope();
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();

            var user = httpContextAccessor.HttpContext?.User;
            if (user == null)
                throw new InvalidOperationException("No user context available.");

            var response = await base.SendAsync(request, cancellationToken);
            
            circuitBreaker.Reset();
            return response;
        }
        catch (HttpRequestException ex)
        {
            circuitBreaker.RecordFailure();
            throw;
        }
        finally{
            // Освобождение не требуется, так как скользящее окно само по себе регулирует лимит
        }
        
        
    }
    private CircuitBreakerState GetCircuitBreaker(string clientName)
        => _circuitBreakers.GetOrAdd(clientName, _ => new CircuitBreakerState(
            maxFailures: 3, 
            breakDuration: TimeSpan.FromMinutes(5)));
    public class CircuitBreakerState
    {
        private int _failures;
        private DateTime? _blockedUntil;
        private readonly int _maxFailures;
        private readonly TimeSpan _breakDuration;

        public bool IsOpen => _blockedUntil != null && DateTime.UtcNow < _blockedUntil;

        public CircuitBreakerState(int maxFailures, TimeSpan breakDuration)
        {
            _maxFailures = maxFailures;
            _breakDuration = breakDuration;
        }

        public void RecordFailure()
        {
            _failures++;
            if (_failures >= _maxFailures)
                _blockedUntil = DateTime.UtcNow.Add(_breakDuration);
        }

        public void Reset()
        {
            _failures = 0;
            _blockedUntil = null;
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

public class CircuitBrokenException : Exception
{
    public CircuitBrokenException(string message): base(message){
    }
}