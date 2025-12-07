using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Chutes;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

[ApiController]
[Route("api/providers/[controller]")]
public class ChutesController : ControllerBase
{
    private readonly IChutesUsageService _usageService;
    private readonly ISettingService _settings;

    public ChutesController(
        IChutesUsageService usageService,
        ISettingService settings)
    {
        _usageService = usageService;
        _settings = settings;
    }

    [HttpGet("usage")]
    public async Task<ActionResult<ChutesUsageSnapshot>> GetUsage([FromQuery] bool refresh = false)
    {
        var model = await _settings.GetSetting(SettingKeys.Translation.Chutes.Model);
        var snapshot = await _usageService.GetUsageSnapshotAsync(model, refresh, HttpContext.RequestAborted);
        return Ok(snapshot);
    }
}
