using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.NanoGpt;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

[ApiController]
[Route("api/providers/[controller]")]
public class NanoGptController : ControllerBase
{
    private readonly INanoGptUsageService _usageService;

    public NanoGptController(INanoGptUsageService usageService)
    {
        _usageService = usageService;
    }

    [HttpGet("usage")]
    public async Task<ActionResult<NanoGptUsageSnapshot>> GetUsage([FromQuery] bool refresh = false)
    {
        var snapshot = await _usageService.GetUsageSnapshotAsync(refresh, HttpContext.RequestAborted);
        return Ok(snapshot);
    }
}
