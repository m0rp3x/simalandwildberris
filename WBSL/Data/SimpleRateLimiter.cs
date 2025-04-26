namespace WBSL.Data;

public class SimpleRateLimiter
{
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxTokens;
    private readonly TimeSpan _refillPeriod;
    private int _availableTokens;
    private Timer _timer;

    public SimpleRateLimiter(int maxTokens, TimeSpan refillPeriod)
    {
        _maxTokens = maxTokens;
        _refillPeriod = refillPeriod;
        _availableTokens = maxTokens;
        _semaphore = new SemaphoreSlim(1, 1);

        _timer = new Timer(RefillTokens, null, refillPeriod, refillPeriod);
    }

    private void RefillTokens(object state)
    {
        _semaphore.Wait();
        _availableTokens = _maxTokens;
        _semaphore.Release();
    }

    public async Task WaitAsync(CancellationToken ct = default)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            await _semaphore.WaitAsync(ct);
            if (_availableTokens > 0)
            {
                _availableTokens--;
                _semaphore.Release();
                return;
            }
            _semaphore.Release();
            await Task.Delay(50, ct); // чуть-чуть подождать, потом попробовать снова
        }
    }
}
