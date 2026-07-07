using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.CrofAi;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

[ApiController]
[Route("api/providers/[controller]")]
public class CrofAiController : ControllerBase
{
    private readonly ICrofAiUsageService _usageService;

    public CrofAiController(ICrofAiUsageService usageService)
    {
        _usageService = usageService;
    }

    [HttpGet("usage")]
    public async Task<ActionResult<CrofAiUsageSnapshot>> GetUsage([FromQuery] bool refresh = false)
    {
        var snapshot = await _usageService.GetUsageSnapshotAsync(refresh, HttpContext.RequestAborted);
        return Ok(snapshot);
    }
}
