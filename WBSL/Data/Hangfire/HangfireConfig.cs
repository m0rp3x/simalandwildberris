using Hangfire;
using Hangfire.PostgreSql;
using WBSL.Data.Services.Simaland;

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
        RecurringJob.AddOrUpdate<SimalandService>(
            "fetch_simaland_balance",
            job => job.SyncProductsBalanceAsync(),
            "*/30 * * * *" // CRON: каждые 30 минут
        );
    }
}