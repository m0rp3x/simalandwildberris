using Hangfire;
using Microsoft.EntityFrameworkCore;
using WBSL.Data.Models;
using WBSL.Data.Services.Wildberries;

namespace WBSL.Data.Services;

public class JobSchedulerService
{
    private readonly QPlannerDbContext _db;
    public JobSchedulerService(QPlannerDbContext db) => _db = db;

    private static readonly Dictionary<string, Action<string>> _registrators = new()
    {
        ["fetch_wb_categories"] = cron =>
            RecurringJob.AddOrUpdate<WildberriesCategoryService>(
                "fetch_wb_categories",
                svc => svc.SyncCategoriesAsync(),
                cron
            ),

        ["fetch_wb_products"] = cron =>
            RecurringJob.AddOrUpdate<WildberriesProductsService>(
                "fetch_wb_products",
                svc => svc.SyncProductsAsync(),
                cron
            ),

        ["orders-fetch-job"] = cron =>
            RecurringJob.AddOrUpdate<WildberriesOrdersProcessingService>(
                "orders-fetch-job",
                svc => svc.FetchAndSaveOrdersAsync(),
                cron
            ),
    };

    
    public async Task SyncSchedulesAsync(){
        var configs = await _db.Set<JobSchedule>().ToListAsync();
        if (!configs.Any()){
            // Заполняем из appsettings, если таблица пустая
            configs = new List<JobSchedule>{
                new(){ JobId = "fetch_wb_categories", CronExpr = "30 1 * * *" },
                new(){ JobId = "fetch_wb_products", CronExpr = "0 2 * * *" },
                new(){ JobId = "orders-fetch-job", CronExpr = Cron.HourInterval(3) },
                // ...
            };
            _db.AddRange(configs);
            await _db.SaveChangesAsync();
        }

        foreach (var cfg in configs)
        {
            if (!_registrators.TryGetValue(cfg.JobId, out var reg))
                throw new InvalidOperationException($"Unknown job «{cfg.JobId}»");

            reg(cfg.CronExpr);
        }
    }

    // Для админа: обновить cron в БД и сразу «перерегистрировать» в Hangfire
    public async Task UpdateCronAsync(string jobId, string newCron){
        var cfg = await _db.Set<JobSchedule>().FindAsync(jobId)
                  ?? throw new KeyNotFoundException(jobId);
        cfg.CronExpr     = newCron;
        cfg.LastUpdated  = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        if (!_registrators.TryGetValue(jobId, out var reg))
            throw new InvalidOperationException($"Unknown job ID «{jobId}»");

        reg(newCron);
    }
}