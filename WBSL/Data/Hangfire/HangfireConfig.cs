using Hangfire;
using Hangfire.PostgreSql;
using WBSL.Data.Services.Wildberries;

namespace WBSL.Data.Hangfire;

public static class HangfireConfig
{
    public static void AddHangfireWithJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHangfire(config =>
            config.UsePostgreSqlStorage(configuration.GetConnectionString("Postgres")));

        services.AddHangfireServer();
        
    }

    public static void RegisterJobs()
    {
        RecurringJob.AddOrUpdate<WildberriesCategoryService>(
            "fetch_wb_categories",
            job => job.SyncCategoriesAsync(),
            "30 1 * * *" // каждый день в 1:30
        );
        
        RecurringJob.AddOrUpdate<WildberriesProductsService>(
            "fetch_wb_products",
            job => job.SyncProductsAsync(),
            "0 2 * * *" // каждый день в 2:00
        );
        
        // RecurringJob.AddOrUpdate<WildberriesOrdersProcessingService>(
        //     /* jobId */        "orders-fetch-job",
        //     /* метод */        svc => svc.FetchAndSaveOrdersAsync(),
        //     /* cron: */         Cron.HourInterval(3)
        // );
    }
}