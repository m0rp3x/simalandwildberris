using Microsoft.EntityFrameworkCore;
using Shared.Enums;
using WBSL.Data.HttpClientFactoryExt;
using WBSL.Data.Models;
using WBSL.Data.Services.Simaland;

namespace WBSL.Data.Services;

public class BalanceUpdateScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    
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

        if (!ShouldRunNow(rule, now))
        {
            return;
        }
        
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
        foreach (var account in distinctWarehouses)
        {
            var wbClient = await factory.CreateClientAsync(ExternalAccountType.WildBerriesMarketPlace, account.id, true);

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
                
                await sima.FetchAndSaveProductsBalance(sids, simaClient, wbClient, ct, account.warehouseid.Value, matchingAccountIds);
            }
            else{
                Console.WriteLine("Warehouse id is null");
            }
        }
    }
    private bool ShouldRunNow(BalanceUpdateRule rule, DateTime now)
    {
        if (rule.UpdateInterval == TimeSpan.Zero)
            return true; // Крутится бесконечно

        var minutesSinceMidnight = (int)now.TimeOfDay.TotalMinutes;
        var intervalMinutes = (int)rule.UpdateInterval.TotalMinutes;

        return minutesSinceMidnight % intervalMinutes == 0;
    }
}