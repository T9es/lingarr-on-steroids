using Hangfire;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
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
    private readonly LingarrDbContext _dbContext;
    private readonly ILogger<AutomatedTranslationJob> _logger;
    private readonly IMediaSubtitleProcessor _mediaSubtitleProcessor;
    private readonly ISettingService _settingService;
    private readonly IScheduleService _scheduleService;
    private readonly IMediaStateService _mediaStateService;

    public AutomatedTranslationJob(
        LingarrDbContext dbContext,
        ILogger<AutomatedTranslationJob> logger,
        IMediaSubtitleProcessor mediaSubtitleProcessor,
        IScheduleService scheduleService,
        ISettingService settingService,
        IMediaStateService mediaStateService)
    {
        _dbContext = dbContext;
        _logger = logger;
        _settingService = settingService;
        _scheduleService = scheduleService;
        _mediaSubtitleProcessor = mediaSubtitleProcessor;
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
                SettingKeys.Automation.MaxTranslationsPerRun,
                SettingKeys.Automation.MovieAgeThreshold,
                SettingKeys.Automation.ShowAgeThreshold
            ]);

            var maxPerRun = int.TryParse(
                settings.GetValueOrDefault(SettingKeys.Automation.MaxTranslationsPerRun), 
                out var limit) ? limit : 10;

            var movieAgeThreshold = TimeSpan.FromHours(
                int.TryParse(settings.GetValueOrDefault(SettingKeys.Automation.MovieAgeThreshold), out var mh) ? mh : 0);
            var showAgeThreshold = TimeSpan.FromHours(
                int.TryParse(settings.GetValueOrDefault(SettingKeys.Automation.ShowAgeThreshold), out var sh) ? sh : 0);

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

                // For stale/unknown/failed items, refresh state first
                TranslationState currentState;
                if (mediaType == MediaType.Movie)
                {
                    currentState = ((Movie)media).TranslationState;
                }
                else
                {
                    currentState = ((Episode)media).TranslationState;
                }

                if (currentState == TranslationState.Stale || currentState == TranslationState.Unknown || currentState == TranslationState.Failed || currentState == TranslationState.AwaitingSource)
                {
                    // For Failed/AwaitingSource items, we allow retry if state re-evaluation says it's Pending
                    var newState = await _mediaStateService.UpdateStateAsync(media, mediaType);
                    
                    // If it's still AwaitingSource, we trigger a probe/index
                    if (newState == TranslationState.AwaitingSource)
                    {
                        _logger.LogInformation("Item {Title} is AwaitingSource, triggering probe/index", media.Title);
                        await _mediaSubtitleProcessor.ProcessMediaForceAsync(
                            media, mediaType,
                            forceProcess: true,
                            forceTranslation: false);
                        
                        // Refresh state after probe
                        newState = await _mediaStateService.UpdateStateAsync(media, mediaType);
                    }

                    if (newState != TranslationState.Pending)
                    {
                        _logger.LogDebug(
                            "Skipping {Title}: state refreshed to {State}",
                            media.Title, newState);
                        continue;
                    }
                }

                // Check age threshold
                var ageThreshold = mediaType == MediaType.Movie ? movieAgeThreshold : showAgeThreshold;
                
                // Check for custom override on movie
                if (mediaType == MediaType.Movie && media is Movie movie && movie.TranslationAgeThreshold.HasValue)
                {
                    ageThreshold = TimeSpan.FromHours(movie.TranslationAgeThreshold.Value);
                }
                // Check for custom override on show
                else if (mediaType == MediaType.Episode && media is Episode episode)
                {
                    var show = episode.Season?.Show;
                    if (show?.TranslationAgeThreshold.HasValue == true)
                    {
                        ageThreshold = TimeSpan.FromHours(show.TranslationAgeThreshold.Value);
                    }
                }
                
                if (!MeetsAgeThreshold(media, ageThreshold))
                {
                    _logger.LogDebug(
                        "Skipping {Title}: does not meet age threshold", 
                        media.Title);
                    continue;
                }

                // Queue translation
                try
                {
                    var count = await _mediaSubtitleProcessor.ProcessMediaForceAsync(
                        media, mediaType,
                        forceProcess: false,
                        forceTranslation: false);

                    if (count > 0)
                    {
                        translationsQueued += count;
                        
                        // Update state to InProgress
                        await _mediaStateService.UpdateStateAsync(media, mediaType);
                        
                        _logger.LogInformation(
                            "Queued {Count} translation(s) for {Title}",
                            count, media.Title);
                    }
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

    private static bool MeetsAgeThreshold(IMedia media, TimeSpan threshold)
    {
        if (threshold == TimeSpan.Zero)
            return true;

        if (media.DateAdded == null)
            return true;

        var age = DateTime.UtcNow - media.DateAdded.Value.ToUniversalTime();
        return age >= threshold;
    }
}
