using Lingarr.Server.Interfaces;
using Lingarr.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

/// <summary>
/// Controller for dashboard data endpoints
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
        IDashboardService dashboardService,
        ILogger<DashboardController> logger)
    {
        _dashboardService = dashboardService;
        _logger = logger;
    }

    /// <summary>
    /// Get job queue status
    /// </summary>
    [HttpGet("jobs")]
    public async Task<ActionResult<JobQueueStatus>> GetJobQueueStatus()
    {
        try
        {
            var status = await _dashboardService.GetJobQueueStatus();
            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get job queue status");
            return StatusCode(500, "Failed to get job queue status");
        }
    }

    /// <summary>
    /// Get API usage statistics
    /// </summary>
    [HttpGet("api-usage")]
    public async Task<ActionResult<ApiUsageStatus>> GetApiUsage()
    {
        try
        {
            var usage = await _dashboardService.GetApiUsage();
            return Ok(usage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get API usage");
            return StatusCode(500, "Failed to get API usage");
        }
    }

    /// <summary>
    /// Get error log
    /// </summary>
    [HttpGet("errors")]
    public async Task<ActionResult<List<ErrorLogEntry>>> GetErrorLog([FromQuery] int limit = 50)
    {
        try
        {
            var errors = await _dashboardService.GetErrorLog(limit);
            return Ok(errors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get error log");
            return StatusCode(500, "Failed to get error log");
        }
    }
}
