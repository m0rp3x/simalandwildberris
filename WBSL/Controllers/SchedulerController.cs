using Microsoft.AspNetCore.Mvc;
using WBSL.Data.Services;
using WBSL.Data.Services.Simaland;

namespace WBSL.Controllers;

[ApiController]
[Route("api/scheduler")]
public class SchedulerController : ControllerBase
{
    private readonly BalanceUpdateScheduler _scheduler;
    private readonly SimalandClientService simalandClientService;

    public SchedulerController(BalanceUpdateScheduler scheduler, SimalandClientService _simalandClientService){
        _scheduler = scheduler;
        simalandClientService = _simalandClientService;
    }

    [HttpGet("balance-enabled")]
    public IActionResult GetBalanceEnabled()
        => Ok(new{ enabled = _scheduler.Enabled });

    [HttpPost("balance-enabled")]
    public async Task<IActionResult> SetBalanceEnabled([FromBody] bool enabled){
        await _scheduler.SetEnabledAsync(enabled);
        return Ok(new{ enabled });
    }

    [HttpPost("balance-reset/{accountId}")]
    public async Task<IActionResult> ResetBalances(int accountId){
        var count = await simalandClientService.ResetBalancesInWbAsync(accountId, CancellationToken.None);
        
        return Ok(new { resetCount = count });
    }
}