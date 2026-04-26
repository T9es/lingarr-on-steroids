using Hangfire;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Server.Hubs;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Jobs;
using Lingarr.Server.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Listener;

public class SettingChangedListener
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IScheduleService _scheduleService;
    private readonly IHubContext<SettingUpdatesHub> _hubContext;
    private readonly ILogger<SettingChangedListener> _logger;
    public SettingChangedListener(IServiceProvider serviceProvider,
        IScheduleService scheduleService,
        IHubContext<SettingUpdatesHub> hubContext,
        ILogger<SettingChangedListener> logger)
    {
        _serviceProvider = serviceProvider;
        _scheduleService = scheduleService;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async void OnSettingChanged(SettingService settingService, string setting)
    {
        var settingGroups = new Dictionary<string, (string actionType, string actionName, string[] keys)>
        {
            {
                "radarr", ("Job", "Radarr", [
                    SettingKeys.Integration.RadarrApiKey,
                    SettingKeys.Integration.RadarrUrl
                ])
            },
            {
                "sonarr", ("Job", "Sonarr", [
                    SettingKeys.Integration.SonarrApiKey,
                    SettingKeys.Integration.SonarrUrl
                ])
            },
            {
                "automation", ("Job", "Automation", [
                    SettingKeys.Automation.AutomationEnabled,
                    SettingKeys.Automation.TranslationSchedule,
                    SettingKeys.Automation.MaxTranslationsPerRun
                ])
            },
            {
                "customSourceSchedule", ("Action", "CustomSourceSchedule", [
                    SettingKeys.Automation.CustomSourceScanSchedule
                ])
            },
            {
                "clearHash", ("Action", "ClearHash", [
                    SettingKeys.Translation.SourceLanguages
                ])
            },
            {
                "schedule", ("Action", "Schedule", [
                    SettingKeys.Automation.MovieSchedule,
                    SettingKeys.Automation.ShowSchedule
                ])
            },
            {
                "serviceType", ("Action", "ServiceType", [
                    SettingKeys.Translation.ServiceType
                ])
            },
            {
                "resumePausedTranslations", ("Action", "ResumePausedTranslations", [
                    SettingKeys.Translation.ServiceType,
                    SettingKeys.Translation.OpenAi.ApiKey,
                    SettingKeys.Translation.OpenAi.Model,
                    SettingKeys.Translation.Anthropic.ApiKey,
                    SettingKeys.Translation.Anthropic.Model,
                    SettingKeys.Translation.Gemini.ApiKey,
                    SettingKeys.Translation.Gemini.Model,
                    SettingKeys.Translation.DeepSeek.ApiKey,
                    SettingKeys.Translation.DeepSeek.Model,
                    SettingKeys.Translation.LocalAi.ApiKey,
                    SettingKeys.Translation.LocalAi.Endpoint,
                    SettingKeys.Translation.LocalAi.Model,
                    SettingKeys.Translation.Chutes.ApiKey,
                    SettingKeys.Translation.Chutes.Model,
                    SettingKeys.Translation.NanoGpt.ApiKey,
                    SettingKeys.Translation.NanoGpt.Model,
                    SettingKeys.Translation.NanoGpt.SubscriptionModelsOnly,
                    SettingKeys.Translation.NanoGpt.WeeklyTokenAllowance,
                    SettingKeys.Translation.NanoGpt.TokenReserve,
                    SettingKeys.Translation.NanoGpt.DailyUnitReserve,
                    SettingKeys.Translation.NanoGpt.MonthlyUnitReserve
                ])
            },
            {
                "batchTranslation", ("Action", "BatchTranslation", [
                    SettingKeys.Translation.UseBatchTranslation
                ])
            },
            {
                "parallelTranslations", ("Action", "ParallelTranslations", [
                    SettingKeys.Translation.MaxParallelTranslations
                ])
            },
            {
                "uploadReservedWorkerSlots", ("Action", "UploadReservedWorkerSlots", [
                    SettingKeys.UploadWorkspace.ReservedWorkerSlots
                ])
            },
            {
                "languageSettings", ("Action", "InvalidateTranslationState", [
                    SettingKeys.Translation.SourceLanguages,
                    SettingKeys.Translation.TargetLanguages,
                    SettingKeys.Translation.IgnoreCaptions,
                    SettingKeys.Translation.SubtitleOutputMode,
                    SettingKeys.SubtitleValidation.SkipWhenTargetEmbedded
                ])
            }
        };

        // Some settings intentionally participate in more than one group.
        // For example, source language changes should clear cached hashes and
        // invalidate translation state in the same pass.
        foreach (var group in settingGroups)
        {
            // Check if the changed setting belongs to this configuration group based on it's *keys*
            if (group.Value.keys.Contains(setting))
            {
                switch (group.Value.actionType)
                {
                    case "Job":
                        await RunJob(group.Value.actionName, group.Value.keys);
                        break;
                    case "Action":
                        await RunAction(group.Value.actionName, group.Value.keys);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// This method retrieves the required settings from the database. If all required settings have non-empty values,
    /// it enqueues the appropriate background job based on the <paramref name="jobName"/>:
    /// </summary>
    /// <param name="jobName">The name of the job to run.</param>
    /// <param name="requiredKeys">An array of setting keys that must have values in the database.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task RunJob(string jobName, string[] requiredKeys)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();
        var settingService = scope.ServiceProvider.GetRequiredService<ISettingService>();

        var settings = await dbContext.Settings
            .Where(s => requiredKeys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        bool allRequiredKeysHaveValues = requiredKeys.All(key =>
            settings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value));

        if (allRequiredKeysHaveValues)
        {
            switch (jobName)
            {
                case "Radarr":
                    _logger.LogInformation(
                        $"Settings changed for |Green|{jobName}|/Green|. All settings are complete, |Orange|indexing media...|/Orange|");

                    await _hubContext.Clients.Group("SettingUpdates").SendAsync("SettingUpdate", new
                    {
                        Key = SettingKeys.Integration.RadarrSettingsCompleted,
                        Value = "true"
                    });

                    await settingService.SetSetting(SettingKeys.Integration.RadarrSettingsCompleted, "true");
                    BackgroundJob.Schedule<SyncMovieJob>(job => job.Execute(), TimeSpan.FromMinutes(1));
                    break;
                case "Sonarr":
                    _logger.LogInformation(
                        $"Settings changed for |Green|{jobName}|/Green|. All settings are complete, |Orange|indexing media...|/Orange|");

                    await _hubContext.Clients.Group("SettingUpdates").SendAsync("SettingUpdate", new
                    {
                        Key = SettingKeys.Integration.SonarrSettingsCompleted,
                        Value = "true"
                    });

                    await settingService.SetSetting(SettingKeys.Integration.SonarrSettingsCompleted, "true");
                    BackgroundJob.Schedule<SyncShowJob>(job => job.Execute(), TimeSpan.FromMinutes(1));
                    break;
                case "Automation":
                    _logger.LogInformation(
                        $"Settings changed for |Green|{jobName}|/Green|. Automation has been |Orange|modified|/Orange|.");
                    await _scheduleService.SyncAutomationJobAsync();
                    break;
            }
        }
    }

    /// <summary>
    /// This method retrieves the required settings from the database. If all required settings have non-empty values,
    /// it performs an action based on the <paramref name="actionName"/>:
    /// </summary>
    /// <param name="actionName">The name of the action to run.</param>
    /// <param name="requiredKeys">An array of setting keys that must have values in the database.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task RunAction(string actionName, string[] requiredKeys)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();
        var settingService = scope.ServiceProvider.GetRequiredService<ISettingService>();

        var settings = await dbContext.Settings
            .Where(s => requiredKeys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        bool allRequiredKeysHaveValues = requiredKeys.All(key =>
            settings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value));

        if (allRequiredKeysHaveValues || actionName == "ResumePausedTranslations")
        {
            switch (actionName)
            {
                case "ClearHash":
                    dbContext.Database.ExecuteSqlRaw("UPDATE movies SET media_hash = ''");
                    dbContext.Database.ExecuteSqlRaw("UPDATE episodes SET media_hash = ''");
                    break;

                case "Schedule":
                    await _scheduleService.SyncIndexerJobsAsync();
                    break;

                case "CustomSourceSchedule":
                    await _scheduleService.SyncCustomSourceScanJobAsync();
                    break;



                case "BatchTranslation":
                    var useBatchTranslation = await settingService.GetSetting(SettingKeys.Translation.UseBatchTranslation);
                    if (useBatchTranslation is "true")
                    {
                        await settingService.SetSetting(SettingKeys.Translation.AiContextPromptEnabled, "false");
                        await _hubContext.Clients.Group("SettingUpdates").SendAsync("SettingUpdate", new
                        {
                            Key = SettingKeys.Translation.AiContextPromptEnabled,
                            Value = "false"
                        });
                    }
                    break;
                    
                case "ParallelTranslations":
                    var workerService = _serviceProvider.GetRequiredService<ITranslationWorkerService>();
                    var maxParallelSetting = await settingService.GetSetting(SettingKeys.Translation.MaxParallelTranslations);
                    var maxParallel = int.TryParse(maxParallelSetting, out var val) && val > 0 ? val : 1;
                    await workerService.ReconfigureWorkersAsync(maxParallel);
                    _logger.LogInformation(
                        "Settings changed for |Green|ParallelTranslations|/Green|. Reconfigured to |Orange|{MaxParallel}|/Orange| concurrent translations.",
                        maxParallel);
                    break;

                case "UploadReservedWorkerSlots":
                    var workerServiceForReservedSlots = _serviceProvider.GetRequiredService<ITranslationWorkerService>();
                    var reservedSlotsSetting = await settingService.GetSetting(SettingKeys.UploadWorkspace.ReservedWorkerSlots);
                    var reservedSlots = int.TryParse(reservedSlotsSetting, out var reservedVal) && reservedVal >= 0
                        ? reservedVal
                        : 0;
                    await workerServiceForReservedSlots.ReconfigureReservedUploadSlotsAsync(reservedSlots);
                    _logger.LogInformation(
                        "Settings changed for |Green|UploadReservedWorkerSlots|/Green|. Reconfigured to |Orange|{ReservedSlots}|/Orange| reserved upload slot(s).",
                        reservedSlots);
                    break;
                    
                case "InvalidateTranslationState":
                    var mediaStateService = scope.ServiceProvider.GetRequiredService<IMediaStateService>();
                    await mediaStateService.IncrementSettingsVersionAsync();
                    await mediaStateService.MarkAllStaleAsync();
                    _logger.LogInformation(
                        "Language settings changed - incremented version and marked all media as stale");
                    break;

                case "ResumePausedTranslations":
                    var pausedResumeService = scope.ServiceProvider.GetRequiredService<IPausedTranslationResumeService>();
                    var resumed = await pausedResumeService.ResumePausedRequestsForProviderChangeAsync(CancellationToken.None);
                    if (resumed > 0)
                    {
                        _logger.LogInformation(
                            "Provider setting changed - resumed {Count} paused translation request(s)",
                            resumed);
                    }
                    break;
            }
        }
    }
}
