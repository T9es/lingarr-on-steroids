using Hangfire;
using Lingarr.Core.Data;
using Lingarr.Core.Enum;
using Lingarr.Server.Filters;
using Lingarr.Server.Hubs;
using Lingarr.Server.Interfaces.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Jobs;

/// <summary>
/// Background job that validates subtitle integrity for all Complete-state media.
/// Reports progress via SignalR and queues corrupt subtitles for re-translation.
/// </summary>
public class BulkIntegrityCheckJob
{
    private readonly LingarrDbContext _dbContext;
    private readonly IMediaSubtitleProcessor _mediaSubtitleProcessor;
    private readonly IHubContext<JobProgressHub> _hubContext;
    private readonly ILogger<BulkIntegrityCheckJob> _logger;

    public BulkIntegrityCheckJob(
        LingarrDbContext dbContext,
        IMediaSubtitleProcessor mediaSubtitleProcessor,
        IHubContext<JobProgressHub> hubContext,
        ILogger<BulkIntegrityCheckJob> logger)
    {
        _dbContext = dbContext;
        _mediaSubtitleProcessor = mediaSubtitleProcessor;
        _hubContext = hubContext;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 120 * 60)] // 2 hours max
    [AutomaticRetry(Attempts = 0)]
    [Queue("system")]
    public async Task Execute()
    {
        var jobName = JobContextFilter.GetCurrentJobTypeName();
        _logger.LogInformation("Bulk integrity check job initiated");

        var stats = new BulkIntegrityStats();

        try
        {
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

                    // ProcessMediaForceAsync returns count of translations queued
                    var queuedCount = await _mediaSubtitleProcessor.ProcessMediaForceAsync(
                        movie, 
                        MediaType.Movie, 
                        forceProcess: true,     // Skip hash check, run validation
                        forceTranslation: false // Only queue corrupt ones
                    );

                    if (queuedCount > 0)
                    {
                        stats.CorruptCount++;
                        stats.QueuedCount += queuedCount;
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

                    var queuedCount = await _mediaSubtitleProcessor.ProcessMediaForceAsync(
                        episode, 
                        MediaType.Episode, 
                        forceProcess: true,
                        forceTranslation: false
                    );

                    if (queuedCount > 0)
                    {
                        stats.CorruptCount++;
                        stats.QueuedCount += queuedCount;
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
            await SendProgress(stats);

            _logger.LogInformation(
                "Bulk integrity check completed: {Processed}/{Total}, Valid: {Valid}, Corrupt: {Corrupt}, Queued: {Queued}",
                stats.ProcessedCount, stats.Total, stats.ValidCount, stats.CorruptCount, stats.QueuedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk integrity check job failed");
            stats.IsComplete = true;
            stats.Error = ex.Message;
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
}

/// <summary>
/// Statistics for bulk integrity check progress.
/// </summary>
public class BulkIntegrityStats
{
    public int Total { get; set; }
    public int TotalMovies { get; set; }
    public int TotalEpisodes { get; set; }
    public int ProcessedCount { get; set; }
    public int ValidCount { get; set; }
    public int CorruptCount { get; set; }
    public int QueuedCount { get; set; }
    public int ErrorCount { get; set; }
    public bool IsComplete { get; set; }
    public string? Error { get; set; }
    
    public double ProgressPercent => Total > 0 ? (double)ProcessedCount / Total * 100 : 0;
}
