using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatisticsController : ControllerBase
{
    private readonly IStatisticsService _statisticsService;

    public StatisticsController(IStatisticsService statisticsService)
    {
        _statisticsService = statisticsService;
    }

    [HttpGet]
    public async Task<ActionResult<Statistics>> GetStatistics()
    {
        var stats = await _statisticsService.GetStatistics();
        return Ok(stats);
    }
    
[HttpGet("daily/{days}")]
    public async Task<ActionResult<IEnumerable<DailyStatistics>>> GetDailyStats(int days = 30)
    {
        var stats = await _statisticsService.GetDailyStatistics(days);
        return Ok(stats);
    }

    [HttpGet("hourly")]
    public async Task<ActionResult<IEnumerable<HourlyStatistics>>> GetHourlyStats([FromQuery] DateTime? date = null)
    {
        var stats = await _statisticsService.GetHourlyStatistics(date);
        return Ok(stats);
    }
}