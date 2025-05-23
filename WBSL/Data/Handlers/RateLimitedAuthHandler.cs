﻿using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;
using WBSL.Data.Config;

namespace WBSL.Data.Handlers;

public class RateLimitedAuthHandler : DelegatingHandler
{
    private readonly int _requestLimit;
    private readonly int _timeRequestLimit;
    private static readonly ConcurrentDictionary<string, RollingWindowRateLimiter> _limiters = new();
    private static readonly ConcurrentDictionary<string, CircuitBreakerState> _circuitBreakers = new();
    private readonly string _clientName;

    private const int MaxRetries = 4;

    private static readonly TimeSpan[] Backoff = new[]{
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60)
    };

    public RateLimitedAuthHandler(RateLimitConfig config, string clientName){
        _requestLimit     = config.RequestLimit;
        _timeRequestLimit = config.TimeRequestLimit;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => SendWithRetriesAsync(request, ct, retryCount: 0);

    private async Task<HttpResponseMessage> SendWithRetriesAsync(
        HttpRequestMessage request, CancellationToken ct, int retryCount){
        if (!request.Options.TryGetValue(new HttpRequestOptionsKey<string>("HttpClientName"), out var clientName))
            clientName = "default";

        var limiter = _limiters.GetOrAdd(clientName,
                                         _ => new RollingWindowRateLimiter(
                                             TimeSpan.FromSeconds(_timeRequestLimit), _requestLimit));
        var circuitBreaker = GetCircuitBreaker(clientName);
        if (circuitBreaker.IsOpen){
            var wait = circuitBreaker.GetRemainingBlockTime();
            if (wait > TimeSpan.Zero){
                await Task.Delay(wait, ct);
                circuitBreaker.Reset();
            }
        }
        
        await limiter.WaitAsync(ct);

        HttpResponseMessage response;
        try{
            response = await base.SendAsync(request, ct);
        }
        catch (HttpRequestException ex){
            circuitBreaker.RecordFailure();

            if (retryCount < MaxRetries){
                // случайный джиттер, чтобы уменьшить «бомбёжку»
                var jittered = AddJitter(Backoff[Math.Min(retryCount, Backoff.Length - 1)]);
                await Task.Delay(jittered, ct);
                return await SendWithRetriesAsync(request, ct, retryCount + 1);
            }

            throw;
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests && retryCount < MaxRetries){
            TimeSpan retryAfter;
            Console.Write($"[RateLimitedAuthHandler] Retry after");
            if (response.Headers.TryGetValues("X-Ratelimit-Retry", out var vals)
                && int.TryParse(vals.First(), out var secs)){
                Console.WriteLine($" {secs}s");
                retryAfter = TimeSpan.FromSeconds(secs);
            }
            else{
                retryAfter = response.Headers.RetryAfter?.Delta
                             ?? Backoff[Math.Min(retryCount, Backoff.Length - 1)];
            }
            
            circuitBreaker.ForceOpen(retryAfter);

            await Task.Delay(retryAfter, ct);
            return await SendWithRetriesAsync(request, ct, retryCount + 1);
        }

        // Если всё ок — сбрасываем цепной «пробойник»
        if ((int)response.StatusCode >= 500){
            circuitBreaker.RecordFailure();
        }
        else{
            // Всё остальное — сброс «пробойника»
            circuitBreaker.Reset();
        }

        return response;
    }

    private TimeSpan AddJitter(TimeSpan baseDelay)
        => baseDelay + TimeSpan.FromMilliseconds(new Random().Next(0, 500));

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

        public CircuitBreakerState(int maxFailures, TimeSpan breakDuration){
            _maxFailures   = maxFailures;
            _breakDuration = breakDuration;
        }

        public void RecordFailure(){
            _failures++;
            if (_failures >= _maxFailures)
                _blockedUntil = DateTime.UtcNow.Add(_breakDuration);
        }

        public void Reset(){
            _failures     = 0;
            _blockedUntil = null;
        }
        public void ForceOpen(TimeSpan duration)
        {
            _blockedUntil = DateTime.UtcNow.Add(duration);
        }

        // Сколько осталось ждать
        public TimeSpan GetRemainingBlockTime()
            => _blockedUntil.HasValue
                ? _blockedUntil.Value - DateTime.UtcNow
                : TimeSpan.Zero;
    }

    public class RollingWindowRateLimiter
    {
        private readonly Queue<DateTime> _requestTimes = new();
        private readonly TimeSpan _window;
        private readonly int _maxRequests;
        private readonly SemaphoreSlim _semaphore = new(1, 1); // Блокировка для потокобезопасности

        public RollingWindowRateLimiter(TimeSpan window, int maxRequests){
            _window      = window;
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
    public CircuitBrokenException(string message) : base(message){
    }
}