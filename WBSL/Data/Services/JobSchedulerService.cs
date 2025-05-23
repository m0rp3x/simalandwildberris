using Hangfire;
using Microsoft.EntityFrameworkCore;
using WBSL.Data.Models;
using WBSL.Data.Services.Wildberries;

namespace WBSL.Data.Services;

public class JobSchedulerService
{
    private readonly QPlannerDbContext _db;
    public JobSchedulerService(QPlannerDbContext db) => _db = db;

    private static readonly Dictionary<string, Action<string>> _registrators = new(){
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

        ["fetch_new_orders_job"] = cron =>
            RecurringJob.AddOrUpdate<WildberriesOrdersProcessingService>(
                "fetch_new_orders_job",
                svc => svc.FetchAndSaveOrdersAsync(),
                cron
            ),
        
        //
        // ["simaland-order-cart-job"] = cron => 
        //     RecurringJob.AddOrUpdate<ICreateOrderCart>(
        //         "simaland-order-cart-job",
        //         svc => svc.TestBus(),
        //         cron),
    };


    public async Task SyncSchedulesAsync(){
        var dbJobs = await _db.HangfireJobSchedules
                              .ToDictionaryAsync(js => js.JobId, StringComparer.OrdinalIgnoreCase);

        var defaultCron = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase){
            ["fetch_wb_categories"]  = "30 1 * * *",
            ["fetch_wb_products"]    = "0 2 * * *",
            ["fetch_new_orders_job"] = Cron.HourInterval(3),
            // ["simaland-order-cart-job"]   = Cron.HourInterval(3) //
        };

        foreach (var kvp in _registrators){
            var jobId = kvp.Key;
            if (!dbJobs.ContainsKey(jobId)){
                // создаём новую запись
                var js = new JobSchedule{
                    JobId       = jobId,
                    CronExpr    = defaultCron[jobId],
                    LastUpdated = DateTime.UtcNow
                };
                _db.HangfireJobSchedules.Add(js);
                dbJobs[jobId] = js;
            }
        }

        await _db.SaveChangesAsync();
        foreach (var cfg in dbJobs.Values){
            if (!_registrators.TryGetValue(cfg.JobId, out var reg)){
                Console.WriteLine($"[Warning] Unknown job ID “{cfg.JobId}”, skipping registration.");
                continue;
            }

            try{
                reg(cfg.CronExpr);
            }
            catch (Exception ex){
                // Если вдруг cron неверный или регистрация упала — логируем, но не бросаем
                Console.WriteLine($"[Error] Failed to register job “{cfg.JobId}”: {ex.Message}");
            }
        }
    }

    // Для админа: обновить cron в БД и сразу «перерегистрировать» в Hangfire
    public async Task UpdateCronAsync(string jobId, string newCron){
        var cfg = await _db.Set<JobSchedule>().FindAsync(jobId)
                  ?? throw new KeyNotFoundException(jobId);
        cfg.CronExpr    = newCron;
        cfg.LastUpdated = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        if (!_registrators.TryGetValue(jobId, out var reg))
            throw new InvalidOperationException($"Unknown job ID «{jobId}»");

        reg(newCron);
    }
}