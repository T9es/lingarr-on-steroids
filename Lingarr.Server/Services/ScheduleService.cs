using Hangfire;
using Hangfire.Storage;
using Lingarr.Core.Configuration;
using Lingarr.Core.Enum;
using Lingarr.Server.Hubs;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.OpenApi.Extensions;

namespace Lingarr.Server.Services;

public class ScheduleService : IScheduleService
{
    private sealed record JobMetadata(
        string DisplayNameKey,
        string? EnabledSettingKey,
        string? ScheduleSettingKey,
        string? DefaultSchedule,
        bool IsEditable);

    private static readonly IReadOnlyDictionary<string, JobMetadata> JobMetadataMap =
        new Dictionary<string, JobMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["AutomatedTranslationJob"] = new("schedule.jobDisplay.automatedTranslation", SettingKeys.Automation.AutomationEnabled, SettingKeys.Automation.TranslationSchedule, null, true),
            ["CustomSourceScanJob"] = new("schedule.jobDisplay.customSources", SettingKeys.Automation.CustomSourceScanEnabled, SettingKeys.Automation.CustomSourceScanSchedule, null, true),
            ["SyncMovieJob"] = new("schedule.jobDisplay.syncMovies", SettingKeys.Automation.MovieSyncEnabled, SettingKeys.Automation.MovieSchedule, null, true),
            ["SyncShowJob"] = new("schedule.jobDisplay.syncShows", SettingKeys.Automation.ShowSyncEnabled, SettingKeys.Automation.ShowSchedule, null, true),
            ["CleanupJob"] = new("schedule.jobDisplay.cleanup", SettingKeys.Maintenance.CleanupEnabled, SettingKeys.Maintenance.CleanupSchedule, "0 0 * * 0", true),
            ["UploadWorkspaceCleanupJob"] = new("schedule.jobDisplay.uploadCleanup", SettingKeys.Maintenance.UploadCleanupEnabled, SettingKeys.Maintenance.UploadCleanupSchedule, "0 * * * *", true),
            ["StatisticsJob"] = new("schedule.jobDisplay.statistics", SettingKeys.Maintenance.StatisticsEnabled, SettingKeys.Maintenance.StatisticsSchedule, "0 0 * * *", true),
            ["RetryFailedRequestsJob"] = new("schedule.jobDisplay.retryFailed", SettingKeys.Maintenance.RetryFailedEnabled, SettingKeys.Maintenance.RetryFailedSchedule, "0 22 * * *", true),
            ["UnknownLanguageDetectionJob"] = new("schedule.jobDisplay.languageDetection", SettingKeys.SubtitleExtraction.DetectUnknownLanguages, SettingKeys.SubtitleExtraction.DetectUnknownLanguagesSchedule, null, true)
        };

    private readonly IHubContext<JobProgressHub> _hubContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IScheduleService> _logger;

    public ScheduleService(
        IHubContext<JobProgressHub> hubContext,
        IServiceProvider serviceProvider,
        ILogger<IScheduleService> logger)
    {
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task Initialize()
    {
        using var scope = _serviceProvider.CreateScope();
        var settingService = scope.ServiceProvider.GetRequiredService<ISettingService>();
        var translationRequestService = scope.ServiceProvider.GetRequiredService<ITranslationRequestService>();

        _logger.LogInformation("Synchronizing all recurring jobs with configured settings.");

        await SyncAutomationJobAsync();
        await SyncCustomSourceScanJobAsync();
        await SyncIndexerJobsAsync();
        await SyncMaintenanceJobsAsync();
        await SyncUnknownLanguageDetectionJobAsync();

        _logger.LogInformation("Starting pending translation requests.");
        await translationRequestService.ResumeTranslationRequests();
    }

    public List<RecurringJobStatus> GetRecurringJobs()
    {
        var monitor = JobStorage.Current.GetMonitoringApi();
        var recurringJobs = JobStorage.Current.GetConnection().GetRecurringJobs();

        return recurringJobs
            .Select(job => MapToJobStatus(job, monitor))
            .OrderBy(j => j.Id)
            .ToList();
    }

    /// <inheritdoc />
    public async Task SyncAutomationJobAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var settingService = scope.ServiceProvider.GetRequiredService<ISettingService>();

        var settings = await settingService.GetSettings([
            SettingKeys.Automation.AutomationEnabled,
            SettingKeys.Automation.TranslationSchedule
        ]);

        var automationEnabled =
            settings.GetValueOrDefault(SettingKeys.Automation.AutomationEnabled) == "true";
        var translationSchedule =
            settings.GetValueOrDefault(SettingKeys.Automation.TranslationSchedule);

        if (automationEnabled && !string.IsNullOrWhiteSpace(translationSchedule))
        {
            _logger.LogDebug("AutomatedTranslationJob schedule: '{Schedule}'", translationSchedule);
            RecurringJob.AddOrUpdate<AutomatedTranslationJob>(
                "AutomatedTranslationJob",
                job => job.Execute(),
                translationSchedule,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        }
        else
        {
            RecurringJob.RemoveIfExists("AutomatedTranslationJob");
        }
    }

    /// <inheritdoc />
    public async Task SyncCustomSourceScanJobAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var settingService = scope.ServiceProvider.GetRequiredService<ISettingService>();

        var (enabled, schedule) = await IsJobEnabled(
            SettingKeys.Automation.CustomSourceScanEnabled,
            SettingKeys.Automation.CustomSourceScanSchedule,
            null,
            settingService);

        if (enabled)
        {
            RecurringJob.AddOrUpdate<CustomSourceScanJob>(
                "CustomSourceScanJob",
                job => job.Execute(),
                schedule!,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        }
        else
        {
            RecurringJob.RemoveIfExists("CustomSourceScanJob");
        }
    }

    /// <inheritdoc />
    public async Task SyncUnknownLanguageDetectionJobAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var settingService = scope.ServiceProvider.GetRequiredService<ISettingService>();

        var settings = await settingService.GetSettings([
            SettingKeys.SubtitleExtraction.DetectUnknownLanguages,
            SettingKeys.SubtitleExtraction.DetectUnknownLanguagesSchedule
        ]);

        var detectEnabled = string.Equals(
            settings.GetValueOrDefault(SettingKeys.SubtitleExtraction.DetectUnknownLanguages),
            "true",
            StringComparison.OrdinalIgnoreCase);
        var schedule = settings.GetValueOrDefault(SettingKeys.SubtitleExtraction.DetectUnknownLanguagesSchedule);

        if (detectEnabled && !string.IsNullOrWhiteSpace(schedule))
        {
            RecurringJob.AddOrUpdate<UnknownLanguageDetectionJob>(
                "UnknownLanguageDetectionJob",
                job => job.Execute(),
                schedule,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        }
        else
        {
            RecurringJob.RemoveIfExists("UnknownLanguageDetectionJob");
        }
    }

    /// <inheritdoc />
    public async Task SyncIndexerJobsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var settingService = scope.ServiceProvider.GetRequiredService<ISettingService>();

        var (movieEnabled, movieSchedule) = await IsJobEnabled(
            SettingKeys.Automation.MovieSyncEnabled,
            SettingKeys.Automation.MovieSchedule,
            null,
            settingService);

        if (movieEnabled)
        {
            RecurringJob.AddOrUpdate<SyncMovieJob>(
                "SyncMovieJob",
                job => job.Execute(),
                movieSchedule!);
        }
        else
        {
            RecurringJob.RemoveIfExists("SyncMovieJob");
        }

        var (showEnabled, showSchedule) = await IsJobEnabled(
            SettingKeys.Automation.ShowSyncEnabled,
            SettingKeys.Automation.ShowSchedule,
            null,
            settingService);

        if (showEnabled)
        {
            RecurringJob.AddOrUpdate<SyncShowJob>(
                "SyncShowJob",
                job => job.Execute(),
                showSchedule!);
        }
        else
        {
            RecurringJob.RemoveIfExists("SyncShowJob");
        }
    }

    /// <inheritdoc />
    public async Task SyncMaintenanceJobsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var settingService = scope.ServiceProvider.GetRequiredService<ISettingService>();

        var (cleanupEnabled, cleanupSchedule) = await IsJobEnabled(
            SettingKeys.Maintenance.CleanupEnabled,
            SettingKeys.Maintenance.CleanupSchedule,
            "0 0 * * 0",
            settingService);

        if (cleanupEnabled)
        {
            RecurringJob.AddOrUpdate<CleanupJob>(
                "CleanupJob",
                job => job.Execute(),
                cleanupSchedule!,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        }
        else
        {
            RecurringJob.RemoveIfExists("CleanupJob");
        }

        var (uploadEnabled, uploadSchedule) = await IsJobEnabled(
            SettingKeys.Maintenance.UploadCleanupEnabled,
            SettingKeys.Maintenance.UploadCleanupSchedule,
            "0 * * * *",
            settingService);

        if (uploadEnabled)
        {
            RecurringJob.AddOrUpdate<UploadWorkspaceCleanupJob>(
                "UploadWorkspaceCleanupJob",
                job => job.Execute(),
                uploadSchedule!,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        }
        else
        {
            RecurringJob.RemoveIfExists("UploadWorkspaceCleanupJob");
        }

        var (statsEnabled, statsSchedule) = await IsJobEnabled(
            SettingKeys.Maintenance.StatisticsEnabled,
            SettingKeys.Maintenance.StatisticsSchedule,
            "0 0 * * *",
            settingService);

        if (statsEnabled)
        {
            RecurringJob.AddOrUpdate<StatisticsJob>(
                "StatisticsJob",
                job => job.Execute(),
                statsSchedule!,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        }
        else
        {
            RecurringJob.RemoveIfExists("StatisticsJob");
        }

        var (retryEnabled, retrySchedule) = await IsJobEnabled(
            SettingKeys.Maintenance.RetryFailedEnabled,
            SettingKeys.Maintenance.RetryFailedSchedule,
            "0 22 * * *",
            settingService);

        if (retryEnabled)
        {
            RecurringJob.AddOrUpdate<RetryFailedRequestsJob>(
                "RetryFailedRequestsJob",
                job => job.Execute(),
                retrySchedule!,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        }
        else
        {
            RecurringJob.RemoveIfExists("RetryFailedRequestsJob");
        }
    }

    /// <summary>
    /// Checks if a job is enabled and has a valid schedule.
    /// </summary>
    private async Task<(bool enabled, string? schedule)> IsJobEnabled(
        string? enabledKey,
        string scheduleKey,
        string? defaultSchedule,
        ISettingService settingService)
    {
        if (enabledKey != null)
        {
            var enabledValue = await settingService.GetSetting(enabledKey);
            if (enabledValue != "true")
            {
                return (false, null);
            }
        }

        var schedule = await settingService.GetSetting(scheduleKey);
        if (string.IsNullOrWhiteSpace(schedule))
        {
            schedule = defaultSchedule;
        }

        return (!string.IsNullOrWhiteSpace(schedule), schedule);
    }

    public string GetJobState(string jobId)
    {
        var monitor = JobStorage.Current.GetMonitoringApi();

        if (monitor.SucceededJobs(0, 1).Any(j => j.Key == jobId))
            return JobStatus.Succeeded.GetDisplayName();
        if (monitor.FailedJobs(0, 1).Any(j => j.Key == jobId))
            return JobStatus.Failed.GetDisplayName();
        if (monitor.ScheduledJobs(0, 1).Any(j => j.Key == jobId))
            return JobStatus.Scheduled.GetDisplayName();
        if (monitor.EnqueuedJobs("default", 0, 1).Any(j => j.Key == jobId))
            return JobStatus.Enqueued.GetDisplayName();

        return JobStatus.Planned.GetDisplayName();
    }

    public async Task UpdateJobState(string jobId, string state)
    {
        try
        {
            await _hubContext.Clients.Group("JobProgress")
                .SendAsync("JobStateUpdated", jobId, state);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating job state for job {JobId}", jobId);
            throw;
        }
    }

    private RecurringJobStatus MapToJobStatus(RecurringJobDto dto, IMonitoringApi monitor)
    {
        var metadata = JobMetadataMap.TryGetValue(dto.Id, out var jobMetadata)
            ? jobMetadata
            : new JobMetadata("schedule.jobDisplay.custom", null, null, null, false);

        var status = new RecurringJobStatus
        {
            Id = dto.Id,
            DisplayNameKey = metadata.DisplayNameKey,
            Cron = dto.Cron,
            Queue = dto.Queue,
            EnabledSettingKey = metadata.EnabledSettingKey,
            ScheduleSettingKey = metadata.ScheduleSettingKey,
            IsEditable = metadata.IsEditable,
            JobMethod = dto.Job?.Method?.Name ?? string.Empty,
            NextExecution = dto.NextExecution,
            LastJobId = dto.LastJobId,
            LastJobState = dto.LastJobState,
            LastExecution = dto.LastExecution,
            CreatedAt = dto.CreatedAt,
            TimeZoneId = dto.TimeZoneId
        };

        if (!string.IsNullOrEmpty(dto.LastJobId))
        {
            var processingJobs = monitor.ProcessingJobs(0, int.MaxValue);
            var currentJob = processingJobs.FirstOrDefault(j =>
                j.Key == dto.LastJobId ||
                (j.Value?.Job?.Args?.Contains(dto.Id) ?? false));

            if (currentJob.Value != null)
            {
                status.IsCurrentlyRunning = true;
                status.CurrentState = "Processing";
                status.CurrentJobId = currentJob.Key;
            }
            else
            {
                status.CurrentState = GetJobState(dto.LastJobId);
            }
        }

        return status;
    }
}
