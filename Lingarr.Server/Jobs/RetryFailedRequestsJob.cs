using Hangfire;
using Lingarr.Core.Configuration;
using Lingarr.Core.Enum;
using Lingarr.Server.Filters;
using Lingarr.Server.Interfaces.Services;
using Microsoft.OpenApi.Extensions;

namespace Lingarr.Server.Jobs;

/// <summary>
/// Background job that periodically retries failed translation requests.
/// </summary>
public class RetryFailedRequestsJob
{
    private readonly ITranslationRequestService _translationRequestService;
    private readonly ILogger<RetryFailedRequestsJob> _logger;
    private readonly IScheduleService _scheduleService;
    private readonly ISettingService _settingService;

    public RetryFailedRequestsJob(
        ITranslationRequestService translationRequestService,
        ILogger<RetryFailedRequestsJob> logger,
        IScheduleService scheduleService,
        ISettingService settingService)
    {
        _translationRequestService = translationRequestService;
        _logger = logger;
        _scheduleService = scheduleService;
        _settingService = settingService;
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
            // Check if automation is enabled - we probably only want to auto-retry if automation is on
            var automationEnabled = await _settingService.GetSetting(SettingKeys.Automation.AutomationEnabled);
            if (automationEnabled != "true")
            {
                _logger.LogInformation("Automation is disabled, skipping retry job");
                await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
                return;
            }

            _logger.LogInformation("Starting scheduled retry of failed translation requests...");
            
            // This service method processes in batches
            var count = await _translationRequestService.RetryAllFailedRequests();
            
            if (count > 0)
            {
                _logger.LogInformation("Successfully requeued {Count} failed requests", count);
            }
            else
            {
                _logger.LogInformation("No failed requests found to retry");
            }

            await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retry translation requests");
            await _scheduleService.UpdateJobState(jobName, JobStatus.Failed.GetDisplayName());
            throw;
        }
    }
}
