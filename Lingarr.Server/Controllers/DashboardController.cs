using Lingarr.Server.Interfaces;
using Lingarr.Server.Models;
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

    /// <summary>
    /// Get dashboard layout
    /// </summary>
    [HttpGet("layout")]
    public async Task<ActionResult<string?>> GetLayout()
    {
        try
        {
            var layout = await _dashboardService.GetDashboardLayout();
            return Ok(layout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get dashboard layout");
            return StatusCode(500, "Failed to get dashboard layout");
        }
    }

    public class DashboardLayoutRequest
    {
        public string LayoutJson { get; set; } = string.Empty;
    }

    /// <summary>
    /// Save dashboard layout
    /// </summary>
    [HttpPut("layout")]
    public async Task<IActionResult> SaveLayout([FromBody] DashboardLayoutRequest request)
    {
        try
        {
            await _dashboardService.SaveDashboardLayout(request.LayoutJson);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save dashboard layout");
            return StatusCode(500, "Failed to save dashboard layout");
        }
    }

/// <summary>
    /// Reset dashboard layout to defaults
    /// </summary>
    [HttpPost("layout/reset")]
    public async Task<IActionResult> ResetLayout()
    {
        try
        {
            await _dashboardService.ResetDashboardLayout();
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset dashboard layout");
            return StatusCode(500, "Failed to reset dashboard layout");
        }
    }

    /// <summary>
    /// Clear all failed jobs
    /// </summary>
    [HttpDelete("jobs/failed")]
    public async Task<IActionResult> ClearFailedJobs()
    {
        try
        {
            var count = await _dashboardService.ClearFailedJobs();
            return Ok(new { cleared = count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear failed jobs");
            return StatusCode(500, "Failed to clear failed jobs");
        }
    }

    /// <summary>
    /// Get failed jobs with pagination
    /// </summary>
    [HttpGet("jobs/failed")]
    public async Task<IActionResult> GetFailedJobs([FromQuery] int offset = 0, [FromQuery] int limit = 10)
    {
        try
        {
            var (jobs, totalCount) = await _dashboardService.GetFailedJobs(offset, limit);
            return Ok(new { jobs, totalCount, hasMore = offset + limit < totalCount });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get failed jobs");
            return StatusCode(500, "Failed to get failed jobs");
        }
    }
}
