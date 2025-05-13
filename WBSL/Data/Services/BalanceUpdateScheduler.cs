using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Shared.Enums;
using WBSL.Data.HttpClientFactoryExt;
using WBSL.Data.Models;
using WBSL.Data.Services.Simaland;

namespace WBSL.Data.Services;

public class BalanceUpdateScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    private static readonly ConcurrentDictionary<int, bool> _runningRules =
        new ConcurrentDictionary<int, bool>();

    private readonly Dictionary<int, DateTime> _nextRuns = new();

    private volatile bool _enabled = false;

    public BalanceUpdateScheduler(IServiceScopeFactory scopeFactory){
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Текущее состояние автозапуска.
    /// </summary>
    public bool Enabled => _enabled;

    /// <summary>
    /// Установить состояние автозапуска в память.
    /// </summary>
    public Task SetEnabledAsync(bool enabled){
        _enabled = enabled;
        Console.WriteLine($"[Scheduler] Balance updates enabled = {_enabled}");
        return Task.CompletedTask;
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken){
        List<BalanceUpdateRule> rules = new();
        DateTime lastRulesUpdate = DateTime.MinValue;

        using (var scope = _scopeFactory.CreateScope()){
            var db = scope.ServiceProvider.GetRequiredService<QPlannerDbContext>();
            rules = await db.BalanceUpdateRules.ToListAsync(stoppingToken);
        }

        var now = DateTime.UtcNow;
        foreach (var rule in rules){
            var first = rule.CreatedAt + rule.UpdateInterval;
            if (first <= now){
                // сколько интервалов уже прошло?
                var delta = now - rule.CreatedAt;
                var count = (long)Math.Ceiling(delta.TotalMilliseconds / rule.UpdateInterval.TotalMilliseconds);
                first = rule.CreatedAt + TimeSpan.FromTicks(rule.UpdateInterval.Ticks * count);
            }

            _nextRuns[rule.Id] = first;
        }

        while (!stoppingToken.IsCancellationRequested){
            if (!_enabled){
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                continue;
            }

            now = DateTime.UtcNow;

            try{
                if ((now - lastRulesUpdate) > TimeSpan.FromMinutes(5)){
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<QPlannerDbContext>();

                    rules = await db.BalanceUpdateRules.ToListAsync(stoppingToken);
                    lastRulesUpdate = now;
                }

                foreach (var rule in rules){
                    if (_nextRuns.TryGetValue(rule.Id, out var next) && now >= next){
                        if (_runningRules.TryAdd(rule.Id, true)){
                            _ = Task.Run(async () => {
                                try{
                                    await ProcessRule(rule, stoppingToken);
                                }
                                finally{
                                    _nextRuns[rule.Id] = next + rule.UpdateInterval;
                                    _runningRules.TryRemove(rule.Id, out _);
                                }
                            }, stoppingToken);
                        }
                    }
                }
            }
            catch (Exception ex){
                Console.WriteLine($"Scheduler error: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessRule(BalanceUpdateRule rule, CancellationToken ct){
        try{
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QPlannerDbContext>();
            var sima = scope.ServiceProvider.GetRequiredService<SimalandClientService>();
            var factory = scope.ServiceProvider.GetRequiredService<PlatformHttpClientFactory>();

            var externalAccounts = await db.external_accounts
                .Where(x => x.platform == ExternalAccountType.Wildberries.ToString())
                .ToListAsync(ct);

            var distinctWarehouses = externalAccounts
                .GroupBy(x => x.warehouseid)
                .Select(g => g.First())
                .ToList();
            var simaClient = await factory.CreateClientAsync(ExternalAccountType.SimaLand, 2, true);
            foreach (var account in distinctWarehouses){
                var wbClient =
                    await factory.CreateClientAsync(ExternalAccountType.WildBerriesMarketPlace, account.id, true);

                // Выбираем товары для данного правила
                var sids = await db.products
                    .Where(p => p.balance >= rule.FromStock && p.balance <= rule.ToStock)
                    .Select(p => p.sid)
                    .ToListAsync(ct);

                if (sids.Count == 0)
                    continue;
                // И запускаем синхронизацию
                if (account.warehouseid.HasValue){
                    var matchingAccountIds = externalAccounts
                        .Where(x => x.warehouseid == account.warehouseid)
                        .Select(x => x.id)
                        .ToList();

                    await sima.FetchAndSaveProductsBalance(sids, simaClient, wbClient, ct, account.warehouseid.Value,
                        matchingAccountIds);
                }
                else{
                    Console.WriteLine("Warehouse id is null");
                }
            }
        }
        finally{
            _runningRules.TryRemove(rule.Id, out _);
        }
    }
}