using Microsoft.AspNetCore.Mvc;
using Shared;
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

    [HttpGet("results")]
    public ActionResult<IReadOnlyDictionary<int, List<WarehouseUpdateResult>>> GetAllResults()
    {
        var results = _scheduler.GetAllResults();
        return Ok(results);
    }
    [HttpGet("running-rules")]
    public ActionResult<IReadOnlyDictionary<int, List<WarehouseUpdateResult>>> GetRunningRules()
    {
        var results = _scheduler.GetRunningRuleIds();
        return Ok(results);
    }

    [HttpGet("results/{ruleId:int}")]
    public ActionResult<List<WarehouseUpdateResult>> GetResultsForRule(int ruleId)
    {
        var list = _scheduler.GetResultsForRule(ruleId);
        if (list == null || !list.Any())
            return NotFound($"No results found for rule {ruleId}.");
        return Ok(list);
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

        return Ok(new{ resetCount = count });
    }
}