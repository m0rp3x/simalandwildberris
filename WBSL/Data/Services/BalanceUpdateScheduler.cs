using Microsoft.EntityFrameworkCore;
using Shared.Enums;
using WBSL.Data.HttpClientFactoryExt;
using WBSL.Data.Models;
using WBSL.Data.Services.Simaland;

namespace WBSL.Data.Services;

public class BalanceUpdateScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Dictionary<int, DateTime> _lastRunTimes = new(); 
    private readonly object _lock = new(); 
    
    public BalanceUpdateScheduler(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        List<BalanceUpdateRule> rules = new();
        DateTime lastRulesUpdate = DateTime.MinValue;
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;

                if ((now - lastRulesUpdate) > TimeSpan.FromMinutes(30))
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<QPlannerDbContext>();

                    rules = await db.BalanceUpdateRules.ToListAsync(stoppingToken);
                    lastRulesUpdate = now;
                }

                var tasks = rules.Select(rule => ProcessRule(rule, stoppingToken));

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Scheduler error: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }
    private async Task ProcessRule(BalanceUpdateRule rule, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        lock (_lock)
        {
            if (_lastRunTimes.TryGetValue(rule.Id, out var lastRun))
            {
                if (rule.UpdateInterval != TimeSpan.Zero && now - lastRun < rule.UpdateInterval)
                {
                    return;
                }
            }
            
            _lastRunTimes[rule.Id] = now;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QPlannerDbContext>();
        var sima = scope.ServiceProvider.GetRequiredService<SimalandClientService>();
        var factory = scope.ServiceProvider.GetRequiredService<PlatformHttpClientFactory>();

        var simaClient = await factory.CreateClientAsync(ExternalAccountType.SimaLand, 2, true);
        var wbClient = await factory.CreateClientAsync(ExternalAccountType.WildBerriesMarketPlace, 1, true);

        var sids = await db.products
            .Where(p => p.balance >= rule.FromStock && p.balance <= rule.ToStock)
            .Select(p => p.sid)
            .ToListAsync(ct);

        if (sids.Count == 0)
            return;

        await sima.FetchAndSaveProductsBalance(sids, simaClient, wbClient, ct);
    }
}