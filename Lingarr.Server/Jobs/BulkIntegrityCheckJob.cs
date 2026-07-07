using Hangfire;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Enum;
using Lingarr.Server.Filters;
using Lingarr.Server.Hubs;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Lingarr.Server.Jobs;

/// <summary>
/// Background job that validates subtitle integrity for all Complete-state media.
/// Reports progress via SignalR and queues corrupt subtitles for re-translation.
/// </summary>
public class BulkIntegrityCheckJob
{
    private readonly LingarrDbContext _dbContext;
    private readonly IMediaSubtitleProcessor _mediaSubtitleProcessor;
    private readonly ISubtitleIntegrityService _integrityService;
    private readonly IHubContext<JobProgressHub> _hubContext;
    private readonly ISettingService _settingService;
    private readonly ILogger<BulkIntegrityCheckJob> _logger;

    public BulkIntegrityCheckJob(
        LingarrDbContext dbContext,
        IMediaSubtitleProcessor mediaSubtitleProcessor,
        ISubtitleIntegrityService integrityService,
        IHubContext<JobProgressHub> hubContext,
        ISettingService settingService,
        ILogger<BulkIntegrityCheckJob> logger)
    {
        _dbContext = dbContext;
        _mediaSubtitleProcessor = mediaSubtitleProcessor;
        _integrityService = integrityService;
        _hubContext = hubContext;
        _settingService = settingService;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 120 * 60)] // 2 hours max
    [AutomaticRetry(Attempts = 0)]
    [Queue("system")]
    public async Task Execute()
    {
        var jobName = JobContextFilter.GetCurrentJobTypeName();
        _logger.LogInformation("Bulk integrity check job initiated");

        var stats = new BulkIntegrityStats { IsRunning = true };
        BulkIntegrityStats.Current = stats;

        try
        {
            var autoQueue = string.Equals(
                await _settingService.GetSetting(SettingKeys.SubtitleValidation.BulkIntegrityAutoQueue),
                "true",
                StringComparison.OrdinalIgnoreCase);
            var maxAutoQueue = int.TryParse(
                await _settingService.GetSetting(SettingKeys.SubtitleValidation.BulkIntegrityMaxAutoQueuePerRun),
                out var parsedMaxAutoQueue)
                ? Math.Max(0, parsedMaxAutoQueue)
                : 25;

            stats.AutoQueueEnabled = autoQueue;
            stats.MaxAutoQueuePerRun = maxAutoQueue;

            // Get all Complete-state movies
            var completedMovieIds = await _dbContext.Movies
                .Where(m => m.TranslationState == TranslationState.Complete)
                .Select(m => m.Id)
                .ToListAsync();

            // Get all Complete-state episodes
            var completedEpisodeIds = await _dbContext.Episodes
                .Where(e => e.TranslationState == TranslationState.Complete)
                .Select(e => e.Id)
                .ToListAsync();

            stats.TotalMovies = completedMovieIds.Count;
            stats.TotalEpisodes = completedEpisodeIds.Count;
            stats.Total = stats.TotalMovies + stats.TotalEpisodes;

            _logger.LogInformation(
                "Bulk integrity check starting: {Movies} movies, {Episodes} episodes",
                stats.TotalMovies, stats.TotalEpisodes);

            await SendProgress(stats);

            // Process movies
            foreach (var movieId in completedMovieIds)
            {
                try
                {
                    var movie = await _dbContext.Movies
                        .Include(m => m.EmbeddedSubtitles)
                        .FirstOrDefaultAsync(m => m.Id == movieId);

                    if (movie == null) continue;

                    var shouldQueue = autoQueue && stats.QueuedCount < maxAutoQueue;
                    var remainingQueueSlots = Math.Max(0, maxAutoQueue - stats.QueuedCount);
                    var mediaFindings = new List<SubtitleIntegrityFinding>();
                    var affectedCount = await _mediaSubtitleProcessor.ProcessMediaForceAsync(
                        movie, 
                        MediaType.Movie, 
                        forceProcess: true,     // Skip hash check, run validation
                        forceTranslation: false, // Only queue corrupt ones
                        forcePriority: false,
                        queueTranslations: shouldQueue,
                        maxTranslationsToQueue: remainingQueueSlots,
                        integrityFindings: mediaFindings
                    );

                    stats.FlaggedItems.AddRange(mediaFindings);

                    if (affectedCount > 0)
                    {
                        stats.CorruptCount++;
                        if (shouldQueue)
                        {
                            stats.QueuedCount += affectedCount;
                        }
                    }
                    else
                    {
                        stats.ValidCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error checking movie {MovieId}", movieId);
                    stats.ErrorCount++;
                }

                stats.ProcessedCount++;
                
                // Send progress every 10 items to avoid flooding
                if (stats.ProcessedCount % 10 == 0)
                {
                    await SendProgress(stats);
                }
            }

            // Process episodes
            foreach (var episodeId in completedEpisodeIds)
            {
                try
                {
                    var episode = await _dbContext.Episodes
                        .Include(e => e.EmbeddedSubtitles)
                        .Include(e => e.Season)
                        .ThenInclude(s => s.Show)
                        .FirstOrDefaultAsync(e => e.Id == episodeId);

                    if (episode == null) continue;

                    var shouldQueue = autoQueue && stats.QueuedCount < maxAutoQueue;
                    var remainingQueueSlots = Math.Max(0, maxAutoQueue - stats.QueuedCount);
                    var mediaFindings = new List<SubtitleIntegrityFinding>();
                    var affectedCount = await _mediaSubtitleProcessor.ProcessMediaForceAsync(
                        episode, 
                        MediaType.Episode, 
                        forceProcess: true,
                        forceTranslation: false,
                        forcePriority: false,
                        queueTranslations: shouldQueue,
                        maxTranslationsToQueue: remainingQueueSlots,
                        integrityFindings: mediaFindings
                    );

                    stats.FlaggedItems.AddRange(mediaFindings);

                    if (affectedCount > 0)
                    {
                        stats.CorruptCount++;
                        if (shouldQueue)
                        {
                            stats.QueuedCount += affectedCount;
                        }
                    }
                    else
                    {
                        stats.ValidCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error checking episode {EpisodeId}", episodeId);
                    stats.ErrorCount++;
                }

                stats.ProcessedCount++;
                
                if (stats.ProcessedCount % 10 == 0)
                {
                    await SendProgress(stats);
                }
            }

            stats.IsComplete = true;
            stats.IsRunning = false;
            await PersistResult(stats);
            await SendProgress(stats);

            _logger.LogInformation(
                "Bulk integrity check completed: {Processed}/{Total}, Valid: {Valid}, Corrupt: {Corrupt}, Queued: {Queued}, IncompleteSubtitles: {Incomplete}",
                stats.ProcessedCount, stats.Total, stats.ValidCount, stats.CorruptCount, stats.QueuedCount, stats.IncompleteSubtitleCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk integrity check job failed");
            stats.IsComplete = true;
            stats.IsRunning = false;
            stats.Error = ex.Message;
            await PersistResult(stats);
            await SendProgress(stats);
            throw;
        }
    }

    private async Task SendProgress(BulkIntegrityStats stats)
    {
        try
        {
            await _hubContext.Clients.Group("JobProgress")
                .SendAsync("BulkIntegrityProgress", stats);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send bulk integrity progress update");
        }
    }

    private async Task PersistResult(BulkIntegrityStats stats)
    {
        try
        {
            await _settingService.SetSetting(
                SettingKeys.SubtitleValidation.LastIntegrityCheckResult,
                JsonSerializer.Serialize(stats, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to persist bulk integrity result");
        }
    }
}

/// <summary>
/// Statistics for bulk integrity check progress.
/// </summary>
public class BulkIntegrityStats
{
    /// <summary>
    /// Static tracker for current job progress - persists across page navigations.
    /// </summary>
    public static BulkIntegrityStats? Current { get; set; }
    
    public int Total { get; set; }
    public int TotalMovies { get; set; }
    public int TotalEpisodes { get; set; }
    public int ProcessedCount { get; set; }
    public int ValidCount { get; set; }
    public int CorruptCount { get; set; }
    public int QueuedCount { get; set; }
    public int ErrorCount { get; set; }
    public bool AutoQueueEnabled { get; set; }
    public int MaxAutoQueuePerRun { get; set; }
    
    /// <summary>
    /// Number of translations with incomplete source subtitles (Forced/Signs-only).
    /// </summary>
    public int IncompleteSubtitleCount { get; set; }
    
    /// <summary>
    /// List of flagged incomplete subtitle issues.
    /// </summary>
    public List<Models.SubtitleTypeCheckResult> IncompleteSubtitles { get; set; } = new();

    /// <summary>
    /// Detailed actionable findings detected by this integrity run.
    /// </summary>
    public List<SubtitleIntegrityFinding> FlaggedItems { get; set; } = new();
    
    public bool IsComplete { get; set; }
    public bool IsRunning { get; set; }
    public string? Error { get; set; }
    
    public double ProgressPercent => Total > 0 ? (double)ProcessedCount / Total * 100 : 0;
}

