using Hangfire;
using Lingarr.Core.Enum;
using Lingarr.Server.Filters;
using Lingarr.Server.Interfaces.Services;
using Microsoft.OpenApi.Extensions;

namespace Lingarr.Server.Jobs;

public class CustomSourceScanJob
{
    private readonly ICustomSourceService _customSourceService;
    private readonly IScheduleService _scheduleService;
    private readonly ILogger<CustomSourceScanJob> _logger;

    public CustomSourceScanJob(
        ICustomSourceService customSourceService,
        IScheduleService scheduleService,
        ILogger<CustomSourceScanJob> logger)
    {
        _customSourceService = customSourceService;
        _scheduleService = scheduleService;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
    [AutomaticRetry(Attempts = 0)]
    [Queue("system")]
    public async Task Execute()
    {
        var jobName = JobContextFilter.GetCurrentJobTypeName();
        await _scheduleService.UpdateJobState(jobName, JobStatus.Processing.GetDisplayName());

        try
        {
            var scanned = await _customSourceService.RescanEnabledSourcesAsync();
            _logger.LogInformation("CustomSourceScanJob completed. Scanned {Count} enabled custom source(s).", scanned);
            await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CustomSourceScanJob failed");
            await _scheduleService.UpdateJobState(jobName, JobStatus.Failed.GetDisplayName());
            throw;
        }
    }
}
