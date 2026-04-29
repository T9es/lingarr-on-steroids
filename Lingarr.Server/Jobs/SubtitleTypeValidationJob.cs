using Hangfire;
using Lingarr.Core.Data;
using Lingarr.Core.Enum;
using Lingarr.Server.Hubs;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Lingarr.Server.Jobs;

public class SubtitleTypeValidationJob
{
    private readonly ISubtitleIntegrityService _integrityService;
    private readonly ISettingService _settingService;
    private readonly LingarrDbContext _dbContext;
    private readonly IHubContext<JobProgressHub> _hubContext;
    private readonly ILogger<SubtitleTypeValidationJob> _logger;

    public SubtitleTypeValidationJob(
        ISubtitleIntegrityService integrityService,
        ISettingService settingService,
        LingarrDbContext dbContext,
        IHubContext<JobProgressHub> hubContext,
        ILogger<SubtitleTypeValidationJob> logger)
    {
        _integrityService = integrityService;
        _settingService = settingService;
        _dbContext = dbContext;
        _hubContext = hubContext;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    [AutomaticRetry(Attempts = 0)]
    [Queue("system")]
    public async Task Execute()
    {
        _logger.LogInformation("Subtitle type validation job started");

        var stats = new SubtitleTypeValidationStats { IsRunning = true };
        SubtitleTypeValidationStats.Current = stats;

        try
        {
            var completedTranslations = await _dbContext.Set<Core.Entities.TranslationRequest>()
                .Where(tr => tr.Status == TranslationStatus.Completed)
                .Where(tr => tr.MediaId != null)
                .ToListAsync();

            stats.Total = completedTranslations.Count;
            _logger.LogInformation("Starting subtitle type validation for {Count} translations", stats.Total);

            await SendProgress(stats);

            var processed = 0;
            foreach (var translation in completedTranslations)
            {
                try
                {
                    stats.ProcessedCount++;

                    var checkResult = await _integrityService.ValidateSubtitleTypeAsync(translation.Id);

                    if (checkResult != null && !checkResult.IsComplete)
                    {
                        stats.IncompleteCount++;
                        stats.FlaggedItems.Add(checkResult);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error validating translation {TranslationId}", translation.Id);
                }

                processed++;

                if (processed % 10 == 0)
                {
                    stats.ProcessedCount = processed;
                    await SendProgress(stats);
                }
            }

            stats.ProcessedCount = processed;
            stats.IsComplete = true;
            stats.IsRunning = false;
            await SendProgress(stats);

            var summary = new SubtitleTypeCheckSummary
            {
                TotalScanned = stats.Total,
                IncompleteCount = stats.IncompleteCount,
                FlaggedItems = stats.FlaggedItems
            };

            await _settingService.SetSetting(
                "subtitle_type_validation_last_result",
                JsonSerializer.Serialize(summary, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

            _logger.LogInformation(
                "Subtitle type validation complete: Scanned {Total}, Found {Incomplete} incomplete",
                stats.Total, stats.IncompleteCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subtitle type validation failed");
            stats.IsComplete = true;
            stats.IsRunning = false;
            stats.Error = ex.Message;
            await SendProgress(stats);
            throw;
        }
    }

    private async Task SendProgress(SubtitleTypeValidationStats stats)
    {
        try
        {
            await _hubContext.Clients.Group("JobProgress")
                .SendAsync("SubtitleTypeValidationProgress", stats);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send subtitle type validation progress update");
        }
    }
}

public class SubtitleTypeValidationStats
{
    public static SubtitleTypeValidationStats? Current { get; set; }

    public int Total { get; set; }
    public int ProcessedCount { get; set; }
    public int IncompleteCount { get; set; }
    public List<SubtitleTypeCheckResult> FlaggedItems { get; set; } = new();
    public bool IsComplete { get; set; }
    public bool IsRunning { get; set; }
    public string? Error { get; set; }

    public double ProgressPercent => Total > 0 ? (double)ProcessedCount / Total * 100 : 0;
}
