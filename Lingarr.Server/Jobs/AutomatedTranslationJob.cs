using Hangfire;
using Lingarr.Core.Configuration;
using Lingarr.Core.Enum;
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
    private readonly ICustomMediaStateService _customMediaStateService;

    public AutomatedTranslationJob(
        IAutomationService automationService,
        ILogger<AutomatedTranslationJob> logger,
        IScheduleService scheduleService,
        ISettingService settingService,
        IMediaStateService mediaStateService,
        ICustomMediaStateService customMediaStateService)
    {
        _automationService = automationService;
        _logger = logger;
        _settingService = settingService;
        _scheduleService = scheduleService;
        _mediaStateService = mediaStateService;
        _customMediaStateService = customMediaStateService;
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
            var automationEnabled = await _settingService.GetSetting(
                SettingKeys.Automation.AutomationEnabled);
            if (automationEnabled != "true")
            {
                _logger.LogInformation("Automation is disabled, skipping run");
                await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
                return;
            }

            var settings = await _settingService.GetSettings([
                SettingKeys.Automation.MaxTranslationsPerRun
            ]);

            var maxPerRun = int.TryParse(
                settings.GetValueOrDefault(SettingKeys.Automation.MaxTranslationsPerRun),
                out var limit)
                ? limit
                : 10;

            var mediaToProcess = await _mediaStateService.GetMediaNeedingTranslationAsync(maxPerRun * 2) ?? [];
            var customItemsToProcess = await _customMediaStateService.GetItemsNeedingTranslationAsync(maxPerRun * 2) ?? [];

            _logger.LogInformation(
                "AutomatedTranslationJob: found {LibraryCount} library candidates and {CustomCount} custom-source candidates needing translation",
                mediaToProcess.Count,
                customItemsToProcess.Count);

            var translationsQueued = 0;
            var processedCount = 0;

            var customWorkItems = customItemsToProcess.Select(item => (
                    Media: (Lingarr.Core.Interfaces.IMedia)item,
                    Type: item.ItemKind == CustomMediaItemKind.Movie ? MediaType.Movie : MediaType.Episode))
                .ToList();
            var preferCustomFirst = await ShouldPreferCustomFirstAsync(
                mediaToProcess.Count,
                customWorkItems.Count);

            foreach (var mediaItem in BuildAutomationCandidateSchedule(
                mediaToProcess,
                customWorkItems,
                preferCustomFirst))
            {
                var media = mediaItem.Media;
                var mediaType = mediaItem.Type;

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
                    _logger.LogWarning(ex, "Failed to process {Title} for translation", media.Title);
                }
            }

            _logger.LogInformation(
                "AutomatedTranslationJob completed: processed {Processed}, queued {Queued} translations",
                processedCount,
                translationsQueued);

            await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AutomatedTranslationJob failed");
            await _scheduleService.UpdateJobState(jobName, JobStatus.Failed.GetDisplayName());
            throw;
        }
    }

    private async Task<bool> ShouldPreferCustomFirstAsync(int libraryCount, int customCount)
    {
        if (libraryCount == 0 || customCount == 0)
        {
            return false;
        }

        var cycle = await _settingService.GetSetting(SettingKeys.Automation.TranslationCycle);
        var preferCustomFirst = string.Equals(cycle, "custom", StringComparison.OrdinalIgnoreCase);
        var nextCycle = preferCustomFirst ? "library" : "custom";
        await _settingService.SetSetting(SettingKeys.Automation.TranslationCycle, nextCycle);
        return preferCustomFirst;
    }

    private static IEnumerable<(Lingarr.Core.Interfaces.IMedia Media, MediaType Type)> BuildAutomationCandidateSchedule(
        IReadOnlyList<(Lingarr.Core.Interfaces.IMedia Media, MediaType Type)> libraryItems,
        IReadOnlyList<(Lingarr.Core.Interfaces.IMedia Media, MediaType Type)> customItems,
        bool preferCustomFirst)
    {
        if (customItems.Count > 0 && libraryItems.Count > 0)
        {
            var maxCount = Math.Max(libraryItems.Count, customItems.Count);
            for (var index = 0; index < maxCount; index++)
            {
                if (preferCustomFirst)
                {
                    if (index < customItems.Count)
                    {
                        yield return customItems[index];
                    }

                    if (index < libraryItems.Count)
                    {
                        yield return libraryItems[index];
                    }
                }
                else
                {
                    if (index < libraryItems.Count)
                    {
                        yield return libraryItems[index];
                    }

                    if (index < customItems.Count)
                    {
                        yield return customItems[index];
                    }
                }
            }

            yield break;
        }

        foreach (var libraryItem in libraryItems)
        {
            yield return libraryItem;
        }

        foreach (var customItem in customItems)
        {
            yield return customItem;
        }
    }
}
