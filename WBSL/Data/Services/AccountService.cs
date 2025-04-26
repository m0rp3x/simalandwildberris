using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Shared.Enums;
using WBSL.Data.Errors;
using WBSL.Models;

namespace WBSL.Data.Services;

public class AccountTokenService
{

    private readonly IServiceProvider _serviceProvider;
    public AccountTokenService(IServiceProvider serviceProvider){
        _serviceProvider = serviceProvider;
    }

    public async Task<external_account?> GetCurrentUserExternalAccountAsync(ClaimsPrincipal User, ExternalAccountType platform){
        var account = await GetExternalAccounts(User, platform);

        if (account == null)
            throw new AccountNotFoundError("Аккаунт не найден или не принадлежит вам.");

        return account;
    }

    public async Task<external_account> GetAccountAsync(ExternalAccountType platform, int? accountId = null, Guid? userId = null, bool isSync = false){
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QPlannerDbContext>();

        external_account? account = null;
        if(platform == ExternalAccountType.WildBerriesMarketPlace || platform == ExternalAccountType.WildBerriesDiscountPrices || platform == ExternalAccountType.WildBerriesCommonApi){
            platform = ExternalAccountType.Wildberries;
        }
        
        if (isSync){
            account = await db.external_accounts
                .FirstOrDefaultAsync(a => a.id == accountId && a.platform == platform.ToString());
        }
        if (userId.HasValue && userId.Value != Guid.Empty ){
            account = await db.external_accounts
                .FirstOrDefaultAsync(a => a.user_id == userId && a.id == accountId && a.platform == platform.ToString());
        }

        if (account == null)
            throw new AccountNotFoundError("Аккаунт не найден или не принадлежит вам.");

        return account;
    }

    private async Task<external_account?> GetExternalAccounts(ClaimsPrincipal User, ExternalAccountType platform){
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QPlannerDbContext>();

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        switch (platform){
            case ExternalAccountType.SimaLand:
                return await db.external_accounts
                    .FirstOrDefaultAsync(a => a.user_id == userId && a.platform == "SimaLand");
            case ExternalAccountType.Wildberries:
                return await db.external_accounts
                    .FirstOrDefaultAsync(a => a.user_id == userId && a.platform == "Wildberries");
            default:
                return null;
        }
    }
}