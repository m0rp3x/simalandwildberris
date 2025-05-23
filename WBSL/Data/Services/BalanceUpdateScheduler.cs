using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Enums;
using WBSL.Data.Extensions;
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
    private bool _firstEnableInit = true;

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

    public List<WarehouseUpdateResult> GetResultsForRule(int ruleId){
        if (_resultsByRule.TryGetValue(ruleId, out var list)){
            return list.ToList();
        }

        return new List<WarehouseUpdateResult>();
    }
    
    public List<int> GetRunningRuleIds()
    {
        return _runningRules.Keys.ToList();
    }

    public IReadOnlyDictionary<int, List<WarehouseUpdateResult>> GetAllResults(){
        return _resultsByRule
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToList()
            );
    }

    private readonly ConcurrentDictionary<int, List<WarehouseUpdateResult>> _resultsByRule
        = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken){
        List<BalanceUpdateRule> rules = new();
        DateTime lastRulesUpdate = DateTime.MinValue;

        using (var scope = _scopeFactory.CreateScope()){
            var db = scope.ServiceProvider.GetRequiredService<QPlannerDbContext>();
            rules = await db.BalanceUpdateRules.ToListAsync(stoppingToken);
        }

        var now = DateTime.UtcNow;
        foreach (var rule in rules)
        {
            var govno = TimeSpan.Parse(rule.UpdateInterval.ToString());
            var first = rule.CreatedAt + govno;
            if (first <= now){
                var delta = now - rule.CreatedAt;
                var count = (long)Math.Ceiling(delta.TotalMilliseconds / govno.TotalMilliseconds);
                first = rule.CreatedAt + TimeSpan.FromTicks(govno.Ticks * count);
            }

            _nextRuns[rule.Id] = first;
        }

        while (!stoppingToken.IsCancellationRequested){
            if (!_enabled){
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                continue;
            }

            now = DateTime.UtcNow;

            if (_firstEnableInit){
                var fireAt = now + TimeSpan.FromSeconds(20);
                foreach (var rule in rules){
                    _nextRuns[rule.Id] = fireAt;
                }

                _firstEnableInit = false;
            }
            
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
                            var capturedRule = rule;
                            _ = Task.Run(async () => {
                                DateTime finishedAt;
                                try{
                                    var infos = await ProcessRule(capturedRule, stoppingToken);
                                    _resultsByRule[capturedRule.Id] = infos;
                                }
                                catch (Exception ex){
                                    Console.WriteLine($"Error in ProcessRule for {capturedRule.Id}: {ex}");
                                }
                                finally{
                                    finishedAt = DateTime.UtcNow;
                                    _nextRuns[rule.Id] = Convert.ToDateTime(finishedAt + TimeSpan.Parse(capturedRule.UpdateInterval.ToString()));
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

    private async Task<List<WarehouseUpdateResult>> ProcessRule(BalanceUpdateRule rule,
        CancellationToken ct){
        var results = new List<WarehouseUpdateResult>();

        try{
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QPlannerDbContext>();
            var sima = scope.ServiceProvider.GetRequiredService<SimalandClientService>();
            var factory = scope.ServiceProvider.GetRequiredService<PlatformHttpClientFactory>();

            var externalAccounts = await db.external_accounts
                .Where(x => x.platform == ExternalAccountType.Wildberries.ToString())
                .ToListAsync(ct);

            var accountsByWarehouse = externalAccounts
                .Where(x => x.warehouseid.HasValue)
                .GroupBy(x => x.warehouseid.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var simaClient = await factory.CreateClientAsync(ExternalAccountType.SimaLand, 2, true);

            foreach (var kvp in accountsByWarehouse){
                int warehouseId = kvp.Key;
                if (warehouseId == 0){
                    continue;
                }

                HttpClient? wbClient = null;
                wbClient = await factory.GetValidClientAsync(
                    ExternalAccountType.WildBerriesMarketPlace,
                    kvp.Value.Select(a => a.id),
                    "/ping",
                    ct);

                if (wbClient == null){
                    continue;
                }
                var sids = await (
                                     from p in db.products.AsNoTracking()
                                     join w in db.WbProductCards.AsNoTracking()
                                         on p.sid.ToString() equals w.VendorCode
                                     where p.balance >= rule.FromStock
                                           && p.balance <= rule.ToStock
                                     select p.sid
                                 )
                                 .Distinct()
                                 .ToListAsync(ct);
                if (sids.Count == 0)
                    continue;

                var matchingAccountIds = kvp.Value
                    .Select(acc => acc.id)
                    .ToList();

                var infos = await sima.FetchAndSaveProductsBalance(
                    sids,
                    simaClient,
                    wbClient,
                    ct,
                    warehouseId,
                    matchingAccountIds);

                results.Add(new WarehouseUpdateResult{
                    WarehouseId = warehouseId,
                    Successful = infos.Successful,
                    Failed = infos.Failed,
                    ProcessedCount = infos.Successful.Count + infos.Failed.Count
                });
            }
        }
        finally{
            _runningRules.TryRemove(rule.Id, out _);
        }

        return results;
    }
}