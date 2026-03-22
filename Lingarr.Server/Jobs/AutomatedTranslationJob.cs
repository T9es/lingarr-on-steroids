using Hangfire;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Filters;
using Lingarr.Server.Interfaces.Services;
using Microsoft.OpenApi.Extensions;

namespace Lingarr.Server.Jobs;

/// <summary>
/// Automated translation job that queries for media needing translation.
/// Uses MediaStateService for efficient querying instead of scanning all media.
/// This is the redesigned version that uses TranslationState tracking.
/// </summary>
public class AutomatedTranslationJob
{
    private readonly IAutomationService _automationService;
    private readonly ILogger<AutomatedTranslationJob> _logger;
    private readonly ISettingService _settingService;
    private readonly IScheduleService _scheduleService;
    private readonly IMediaStateService _mediaStateService;

    public AutomatedTranslationJob(
        IAutomationService automationService,
        ILogger<AutomatedTranslationJob> logger,
        IScheduleService scheduleService,
        ISettingService settingService,
        IMediaStateService mediaStateService)
    {
        _automationService = automationService;
        _logger = logger;
        _settingService = settingService;
        _scheduleService = scheduleService;
        _mediaStateService = mediaStateService;
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
            // Check if automation is enabled
            var automationEnabled = await _settingService.GetSetting(SettingKeys.Automation.AutomationEnabled);
            if (automationEnabled != "true")
            {
                _logger.LogInformation("Automation is disabled, skipping run");
                await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
                return;
            }

            // Get settings
            var settings = await _settingService.GetSettings([
                SettingKeys.Automation.MaxTranslationsPerRun
            ]);

            var maxPerRun = int.TryParse(
                settings.GetValueOrDefault(SettingKeys.Automation.MaxTranslationsPerRun), 
                out var limit) ? limit : 10;

            // Get media that needs work (efficient query using TranslationState)
            var mediaToProcess = await _mediaStateService.GetMediaNeedingTranslationAsync(maxPerRun * 2);
            
            _logger.LogInformation(
                "AutomatedTranslationJob: found {Count} candidates needing translation", 
                mediaToProcess.Count);

            var translationsQueued = 0;
            var processedCount = 0;

            foreach (var (media, mediaType) in mediaToProcess)
            {
                if (translationsQueued >= maxPerRun)
                {
                    _logger.LogInformation("Reached max translations per run ({Max}), stopping", maxPerRun);
                    break;
                }

                processedCount++;

                try
                {
                    var count = await _automationService.ProcessLoadedMediaForAutomationAsync(
                        media,
                        mediaType,
                        "fallback_schedule",
                        updateRotationTimestamp: true);
                    translationsQueued += count;
                }
                catch (DirectoryNotFoundException)
                {
                    _logger.LogWarning("Directory not found at path: |Red|{Path}|/Red|, skipping", media.Path);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, 
                        "Failed to process {Title} for translation", 
                        media.Title);
                }
            }

            _logger.LogInformation(
                "AutomatedTranslationJob completed: processed {Processed}, queued {Queued} translations",
                processedCount, translationsQueued);

            await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AutomatedTranslationJob failed");
            await _scheduleService.UpdateJobState(jobName, JobStatus.Failed.GetDisplayName());
            throw;
        }
    }
}
