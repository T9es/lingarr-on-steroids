using Hangfire;
using Lingarr.Core.Data;
using Lingarr.Core.Enum;
using Lingarr.Server.Hubs;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Jobs;

public class VerifyAssIntegrityJob
{
    private readonly ISubtitleService _subtitleService;
    private readonly ISettingService _settingService;
    private readonly LingarrDbContext _dbContext;
    private readonly IHubContext<JobProgressHub> _hubContext;
    private readonly ILogger<VerifyAssIntegrityJob> _logger;

    public VerifyAssIntegrityJob(
        ISubtitleService subtitleService,
        ISettingService settingService,
        LingarrDbContext dbContext,
        IHubContext<JobProgressHub> hubContext,
        ILogger<VerifyAssIntegrityJob> logger)
    {
        _subtitleService = subtitleService;
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
        _logger.LogInformation("ASS Integrity verification job started");

        var stats = new AssVerificationStats { IsRunning = true };
        AssVerificationStats.Current = stats;

        try
        {
            var result = new AssVerificationResult();

            var drawingPattern = new System.Text.RegularExpressions.Regex(
                @"^\s*m\s+-?\d+(\.\d+)?\s+-?\d+(\.\d+)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            const int suspiciousThreshold = 2;

            var queuedRequests = await _dbContext.Set<Core.Entities.TranslationRequest>()
                .Where(tr => tr.Status == TranslationStatus.Pending ||
                             tr.Status == TranslationStatus.InProgress)
                .Where(tr => tr.MediaId != null)
                .Select(tr => new { tr.MediaId, tr.MediaType })
                .ToListAsync();

            var queuedMovieIds = queuedRequests
                .Where(r => r.MediaType == MediaType.Movie)
                .Select(r => r.MediaId!.Value)
                .ToHashSet();

            var queuedEpisodeIds = queuedRequests
                .Where(r => r.MediaType == MediaType.Episode)
                .Select(r => r.MediaId!.Value)
                .ToHashSet();

            var movies = await _dbContext.Movies
                .Where(m => m.Path != null)
                .Select(m => new { m.Id, m.Title, m.Path, m.FileName })
                .ToListAsync();

            var episodes = await _dbContext.Episodes
                .Include(e => e.Season)
                .ThenInclude(s => s.Show)
                .Where(e => e.Path != null)
                .Select(e => new
                {
                    e.Id,
                    Title = $"{e.Season.Show.Title} S{e.Season.SeasonNumber:D2}E{e.EpisodeNumber:D2}",
                    e.Path,
                    e.FileName
                })
                .ToListAsync();

            stats.Total = movies.Count + episodes.Count;
            _logger.LogInformation("ASS verification: {MovieCount} movies, {EpisodeCount} episodes", movies.Count, episodes.Count);

            await SendProgress(stats);

            var processed = 0;

            foreach (var movie in movies)
            {
                try
                {
                    var subtitleFiles = await GetTranslatedSubtitlesForMedia(movie.Path!, movie.FileName!);
                    foreach (var subPath in subtitleFiles)
                    {
                        result.TotalFilesScanned++;
                        var (count, lines) = await GetSuspiciousLines(subPath, drawingPattern);

                        if (count >= suspiciousThreshold)
                        {
                            result.FilesWithDrawings++;
                            result.FlaggedItems.Add(new AssVerificationItem
                            {
                                MediaId = movie.Id,
                                MediaType = "Movie",
                                MediaTitle = movie.Title ?? "Unknown",
                                SubtitlePath = subPath,
                                SuspiciousLineCount = count,
                                SuspiciousLines = lines,
                                Dismissed = false,
                                IsQueued = queuedMovieIds.Contains(movie.Id)
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing movie {MovieId}", movie.Id);
                }

                processed++;
                stats.ProcessedCount = processed;

                if (processed % 10 == 0)
                {
                    await SendProgress(stats);
                }
            }

            foreach (var episode in episodes)
            {
                try
                {
                    var subtitleFiles = await GetTranslatedSubtitlesForMedia(episode.Path!, episode.FileName!);
                    foreach (var subPath in subtitleFiles)
                    {
                        result.TotalFilesScanned++;
                        var (count, lines) = await GetSuspiciousLines(subPath, drawingPattern);

                        if (count >= suspiciousThreshold)
                        {
                            result.FilesWithDrawings++;
                            result.FlaggedItems.Add(new AssVerificationItem
                            {
                                MediaId = episode.Id,
                                MediaType = "Episode",
                                MediaTitle = episode.Title,
                                SubtitlePath = subPath,
                                SuspiciousLineCount = count,
                                SuspiciousLines = lines,
                                Dismissed = false,
                                IsQueued = queuedEpisodeIds.Contains(episode.Id)
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing episode {EpisodeId}", episode.Id);
                }

                processed++;
                stats.ProcessedCount = processed;

                if (processed % 10 == 0)
                {
                    await SendProgress(stats);
                }
            }

            stats.IsComplete = true;
            stats.IsRunning = false;
            await SendProgress(stats);

            await _settingService.SetSetting(
                "subtitle_ass_verification_last_result",
                System.Text.Json.JsonSerializer.Serialize(result));

            _logger.LogInformation(
                "ASS Integrity verification complete: Scanned {Total}, Found {Flagged}",
                result.TotalFilesScanned, result.FilesWithDrawings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ASS Integrity verification failed");
            stats.IsComplete = true;
            stats.IsRunning = false;
            stats.Error = ex.Message;
            await SendProgress(stats);
            throw;
        }
    }

    private async Task<List<string>> GetTranslatedSubtitlesForMedia(string mediaPath, string mediaFileName)
    {
        var subtitleFiles = new List<string>();
        try
        {
            var allSubs = await _subtitleService.GetAllSubtitles(mediaPath);
            subtitleFiles = allSubs
                .Where(s => s.FileName.StartsWith(mediaFileName + ".") || s.FileName == mediaFileName)
                .Select(s => s.Path)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting subtitles for {Path}", mediaPath);
        }
        return subtitleFiles;
    }

    private async Task<(int count, List<string> lines)> GetSuspiciousLines(string subtitlePath, System.Text.RegularExpressions.Regex pattern)
    {
        try
        {
            var lines = await File.ReadAllLinesAsync(subtitlePath);
            var suspiciousLines = lines
                .Where(line => pattern.IsMatch(line.Trim()))
                .Take(10)
                .Select(line => line.Trim().Length > 80 ? line.Trim()[..80] + "..." : line.Trim())
                .ToList();
            return (suspiciousLines.Count, suspiciousLines);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading subtitle file {Path}", subtitlePath);
            return (0, new List<string>());
        }
    }

    private async Task SendProgress(AssVerificationStats stats)
    {
        try
        {
            await _hubContext.Clients.Group("JobProgress")
                .SendAsync("AssVerificationProgress", stats);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send ASS verification progress update");
        }
    }
}

public class AssVerificationStats
{
    public static AssVerificationStats? Current { get; set; }

    public int Total { get; set; }
    public int ProcessedCount { get; set; }
    public bool IsComplete { get; set; }
    public bool IsRunning { get; set; }
    public string? Error { get; set; }

    public double ProgressPercent => Total > 0 ? (double)ProcessedCount / Total * 100 : 0;
}