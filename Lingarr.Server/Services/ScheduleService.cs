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
    private sealed record JobMetadata(string DisplayNameKey, string? ScheduleSettingKey, bool IsEditable);

private static readonly IReadOnlyDictionary<string, JobMetadata> JobMetadataMap =
        new Dictionary<string, JobMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["AutomatedTranslationJob"] = new("schedule.jobDisplay.automatedTranslation", SettingKeys.Automation.TranslationSchedule, true),
            ["CustomSourceScanJob"] = new("schedule.jobDisplay.customSources", SettingKeys.Automation.CustomSourceScanSchedule, true),
            ["SyncMovieJob"] = new("schedule.jobDisplay.syncMovies", SettingKeys.Automation.MovieSchedule, true),
            ["SyncShowJob"] = new("schedule.jobDisplay.syncShows", SettingKeys.Automation.ShowSchedule, true),
            ["CleanupJob"] = new("schedule.jobDisplay.cleanup", null, false),
            ["UploadWorkspaceCleanupJob"] = new("schedule.jobDisplay.cleanup", null, false),
            ["StatisticsJob"] = new("schedule.jobDisplay.statistics", null, false),
            ["RetryFailedRequestsJob"] = new("schedule.jobDisplay.retryFailed", null, false),
            ["UnknownLanguageDetectionJob"] = new("schedule.jobDisplay.languageDetection", SettingKeys.SubtitleExtraction.DetectUnknownLanguagesSchedule, true)
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

        _logger.LogInformation("Configuring media indexers.");
        await SyncIndexerJobsAsync();
        await SyncAutomationJobAsync();
        await SyncCustomSourceScanJobAsync();

        RecurringJob.AddOrUpdate<CleanupJob>(
            "CleanupJob",
            job => job.Execute(),
            Cron.Weekly,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        
        RecurringJob.AddOrUpdate<UploadWorkspaceCleanupJob>(
            "UploadWorkspaceCleanupJob",
            job => job.Execute(),
            Cron.Hourly,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        RecurringJob.AddOrUpdate<StatisticsJob>(
            "StatisticsJob",
            job => job.Execute(),
            Cron.Daily,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        RecurringJob.AddOrUpdate<RetryFailedRequestsJob>(
            "RetryFailedRequestsJob",
            job => job.Execute(),
            Cron.Daily(22),
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

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
            return;
        }

        RecurringJob.RemoveIfExists("AutomatedTranslationJob");
    }

/// <inheritdoc />
    public async Task SyncCustomSourceScanJobAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var settingService = scope.ServiceProvider.GetRequiredService<ISettingService>();

        var scanSchedule = await settingService.GetSetting(SettingKeys.Automation.CustomSourceScanSchedule);
        if (!string.IsNullOrWhiteSpace(scanSchedule))
        {
            RecurringJob.AddOrUpdate<CustomSourceScanJob>(
                "CustomSourceScanJob",
                job => job.Execute(),
                scanSchedule,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
            return;
        }

        RecurringJob.RemoveIfExists("CustomSourceScanJob");
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
            return;
        }

        RecurringJob.RemoveIfExists("UnknownLanguageDetectionJob");
    }

    /// <inheritdoc />
    public async Task SyncIndexerJobsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var settingService = scope.ServiceProvider.GetRequiredService<ISettingService>();

        var settings = await settingService.GetSettings([
            SettingKeys.Automation.MovieSchedule,
            SettingKeys.Automation.ShowSchedule
        ]);

        var movieSchedule = settings.GetValueOrDefault(SettingKeys.Automation.MovieSchedule);
        if (!string.IsNullOrWhiteSpace(movieSchedule))
        {
            RecurringJob.AddOrUpdate<SyncMovieJob>(
                "SyncMovieJob",
                job => job.Execute(),
                movieSchedule);
        }

        var showSchedule = settings.GetValueOrDefault(SettingKeys.Automation.ShowSchedule);
        if (!string.IsNullOrWhiteSpace(showSchedule))
        {
            RecurringJob.AddOrUpdate<SyncShowJob>(
                "SyncShowJob",
                job => job.Execute(),
                showSchedule);
        }
    }

    public string GetJobState(string jobId)
    {
        var monitor = JobStorage.Current.GetMonitoringApi();

        // Check each possible state
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
            : new JobMetadata("schedule.jobDisplay.custom", null, false);

        var status = new RecurringJobStatus
        {
            Id = dto.Id,
            DisplayNameKey = metadata.DisplayNameKey,
            Cron = dto.Cron,
            Queue = dto.Queue,
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

        // Check if there's a currently running job for this recurring job
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
                // Check other states if not processing
                status.CurrentState = GetJobState(dto.LastJobId);
            }
        }

        return status;
    }
}
