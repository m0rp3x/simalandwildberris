using Microsoft.EntityFrameworkCore;

namespace WBSL.Data;

public class ManualDbContextFactory : IDbContextFactory<QPlannerDbContext>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ManualDbContextFactory(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public QPlannerDbContext CreateDbContext()
    {
        var scope = _scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<QPlannerDbContext>();
    }
}
