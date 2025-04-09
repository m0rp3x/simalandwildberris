using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Shared.Enums;
using WBSL.Data.Enums;
using WBSL.Data.Errors;
using WBSL.Models;

namespace WBSL.Data.Services;

public class AccountTokenService
{
    private readonly QPlannerDbContext _db;

    public AccountTokenService(QPlannerDbContext db){
        _db = db;
    }

    public async Task<external_account?> GetCurrentUserExternalAccountAsync(ClaimsPrincipal User, ExternalAccountType platform){
        var account = await GetExternalAccounts(User, platform);

        if (account == null)
            throw new AccountNotFoundError("Аккаунт не найден или не принадлежит вам.");

        return account;
    }

    public async Task<external_account> GetAccountAsync(ExternalAccountType platform, int? accountId = null, Guid? userId = null){
        
        external_account? account;
        if (userId.HasValue && userId.Value != Guid.Empty ){
            account = await _db.external_accounts
                .FirstOrDefaultAsync(a => a.user_id == userId && a.id == accountId && a.platform == platform.ToString());
        }
        else{
            account = await _db.external_accounts
                .FirstOrDefaultAsync(a => a.id == accountId && a.platform == platform.ToString());
        }

        if (account == null && userId.HasValue && userId.Value != Guid.Empty){
            account = await _db.external_accounts
                .FirstOrDefaultAsync(a => a.user_id == userId && a.platform == platform.ToString());
        }
        

        if (account == null)
            throw new AccountNotFoundError("Аккаунт не найден или не принадлежит вам.");

        return account;
    }

    private async Task<external_account?> GetExternalAccounts(ClaimsPrincipal User, ExternalAccountType platform){
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        switch (platform){
            case ExternalAccountType.SimaLand:
                return await _db.external_accounts
                    .FirstOrDefaultAsync(a => a.user_id == userId && a.platform == "SimaLand");
            case ExternalAccountType.Wildberries:
                return await _db.external_accounts
                    .FirstOrDefaultAsync(a => a.user_id == userId && a.platform == "Wildberries");
            default:
                return null;
        }
    }
}