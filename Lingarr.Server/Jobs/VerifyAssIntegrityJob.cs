using Hangfire;
using System.Text.Json;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Hubs;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Jobs;

public class VerifyAssIntegrityJob
{
    private readonly ISubtitleService _subtitleService;
    private readonly ISettingService _settingService;
    private readonly LingarrDbContext _dbContext;
    private readonly IHubContext<JobProgressHub> _hubContext;
    private readonly ISubtitleOutputBackfillService _subtitleOutputBackfillService;
    private readonly ISourceSubtitleResolver _sourceSubtitleResolver;
    private readonly ILogger<VerifyAssIntegrityJob> _logger;

    public VerifyAssIntegrityJob(
        ISubtitleService subtitleService,
        ISettingService settingService,
        LingarrDbContext dbContext,
        IHubContext<JobProgressHub> hubContext,
        ISubtitleOutputBackfillService subtitleOutputBackfillService,
        ISourceSubtitleResolver sourceSubtitleResolver,
        ILogger<VerifyAssIntegrityJob> logger)
    {
        _subtitleService = subtitleService;
        _settingService = settingService;
        _dbContext = dbContext;
        _hubContext = hubContext;
        _subtitleOutputBackfillService = subtitleOutputBackfillService;
        _sourceSubtitleResolver = sourceSubtitleResolver;
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
            var flaggedByPath = new Dictionary<string, AssVerificationItem>(StringComparer.OrdinalIgnoreCase);
            var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var subtitleTag = await _settingService.GetSetting(SettingKeys.Translation.SubtitleTag) ?? string.Empty;
            var subtitleTagShort = await _settingService.GetSetting(SettingKeys.Translation.SubtitleTagShort) ?? string.Empty;

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

            var completedTranslations = await _dbContext.Set<Core.Entities.TranslationRequest>()
                .Where(tr => tr.Status == TranslationStatus.Completed)
                .Where(tr => tr.MediaId != null)
                .Where(tr => tr.SubtitleToTranslate != null && tr.TranslatedSubtitle != null)
                .ToListAsync();

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

            stats.Total = movies.Count + episodes.Count + completedTranslations.Count;
            _logger.LogInformation(
                "ASS verification: {MovieCount} movies, {EpisodeCount} episodes, {TranslationCount} completed translations",
                movies.Count,
                episodes.Count,
                completedTranslations.Count);

            await SendProgress(stats);

            var processed = 0;

            foreach (var movie in movies)
            {
                try
                {
                    var subtitleFiles = await GetTranslatedSubtitlesForMedia(movie.Path!, movie.FileName!);
                    foreach (var subPath in subtitleFiles)
                    {
                        if (scannedPaths.Add(subPath))
                        {
                            result.TotalFilesScanned++;
                        }

                        var scan = await GetSuspiciousLines(subPath);
                        if (scan.HasIssues)
                        {
                            AddOrMergeFinding(
                                flaggedByPath,
                                movie.Id,
                                "Movie",
                                movie.Title ?? "Unknown",
                                subPath,
                                queuedMovieIds.Contains(movie.Id),
                                scan);
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
                        if (scannedPaths.Add(subPath))
                        {
                            result.TotalFilesScanned++;
                        }

                        var scan = await GetSuspiciousLines(subPath);
                        if (scan.HasIssues)
                        {
                            AddOrMergeFinding(
                                flaggedByPath,
                                episode.Id,
                                "Episode",
                                episode.Title,
                                subPath,
                                queuedEpisodeIds.Contains(episode.Id),
                                scan);
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

            foreach (var translation in completedTranslations)
            {
                try
                {
                    if (translation.MediaId == null ||
                        string.IsNullOrWhiteSpace(translation.SubtitleToTranslate) ||
                        string.IsNullOrWhiteSpace(translation.TranslatedSubtitle) ||
                        !File.Exists(translation.TranslatedSubtitle))
                    {
                        continue;
                    }

                    if (scannedPaths.Add(translation.TranslatedSubtitle))
                    {
                        result.TotalFilesScanned++;
                    }

                    var sourceSubtitlePath = await _sourceSubtitleResolver.ResolveReadableSourcePathAsync(
                        translation,
                        CancellationToken.None);
                    var sourceSubtitles = !string.IsNullOrWhiteSpace(sourceSubtitlePath) && File.Exists(sourceSubtitlePath)
                        ? await _subtitleService.ReadSubtitles(sourceSubtitlePath)
                        : [];
                    var targetSubtitles = await _subtitleService.ReadSubtitles(translation.TranslatedSubtitle);
                    var scan = sourceSubtitles.Count > 0
                        ? AssSubtitleArtifactDetector.CompareTagStructure(
                            sourceSubtitles,
                            targetSubtitles,
                            translation.TranslatedSubtitle)
                        : new AssArtifactScanResult();
                    scan.Merge(AssSubtitleArtifactDetector.DetectInlineTagPlacementArtifacts(
                        targetSubtitles.SelectMany(item => item.Lines)));
                    var generatedOutputScans = await ScanGeneratedOutputArtifactsAsync(
                        translation,
                        scannedPaths,
                        result);

                    if (!scan.HasIssues && generatedOutputScans.Count == 0)
                    {
                        continue;
                    }

                    if (ShouldAttemptLocalRepair(scan) ||
                        generatedOutputScans.Any(output => ShouldAttemptLocalRepair(output.Scan)))
                    {
                        var repairResult = await TryRepairExistingAssOutputsAsync(
                            translation,
                            subtitleTag,
                            subtitleTagShort);
                        result.LocallyRepairedFiles += repairResult.RepairedFiles;
                        result.LocalRepairSkippedFiles += repairResult.BackfillSkippedFiles;

                        if (repairResult.RepairedFiles > 0)
                        {
                            foreach (var outputPath in GetGeneratedOutputPaths(translation))
                            {
                                flaggedByPath.Remove(outputPath);
                            }

                            sourceSubtitlePath = await _sourceSubtitleResolver.ResolveReadableSourcePathAsync(
                                translation,
                                CancellationToken.None);
                            sourceSubtitles = !string.IsNullOrWhiteSpace(sourceSubtitlePath) && File.Exists(sourceSubtitlePath)
                                ? await _subtitleService.ReadSubtitles(sourceSubtitlePath)
                                : [];
                            targetSubtitles = await _subtitleService.ReadSubtitles(translation.TranslatedSubtitle);
                            scan = sourceSubtitles.Count > 0
                                ? AssSubtitleArtifactDetector.CompareTagStructure(
                                    sourceSubtitles,
                                    targetSubtitles,
                                    translation.TranslatedSubtitle)
                                : new AssArtifactScanResult();
                            scan.Merge(AssSubtitleArtifactDetector.DetectInlineTagPlacementArtifacts(
                                targetSubtitles.SelectMany(item => item.Lines)));
                            generatedOutputScans = await ScanGeneratedOutputArtifactsAsync(
                                translation,
                                scannedPaths,
                                result);

                            if (!scan.HasIssues && generatedOutputScans.Count == 0)
                            {
                                continue;
                            }
                        }
                    }

                    var isQueued = translation.MediaType == MediaType.Movie
                        ? queuedMovieIds.Contains(translation.MediaId.Value)
                        : queuedEpisodeIds.Contains(translation.MediaId.Value);

                    if (scan.HasIssues)
                    {
                        AddOrMergeFinding(
                            flaggedByPath,
                            translation.MediaId.Value,
                            translation.MediaType.ToString(),
                            translation.Title,
                            translation.TranslatedSubtitle,
                            isQueued,
                            scan);
                    }

                    foreach (var outputScan in generatedOutputScans.Where(output => output.Scan.HasIssues))
                    {
                        AddOrMergeFinding(
                            flaggedByPath,
                            translation.MediaId.Value,
                            translation.MediaType.ToString(),
                            translation.Title,
                            outputScan.Path,
                            isQueued,
                            outputScan.Scan);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Error comparing ASS tag structure for translation target {Path}",
                        translation.TranslatedSubtitle);
                }
                finally
                {
                    processed++;
                    stats.ProcessedCount = processed;

                    if (processed % 10 == 0)
                    {
                        await SendProgress(stats);
                    }
                }
            }

            result.FlaggedItems = flaggedByPath.Values
                .OrderBy(item => item.MediaTitle)
                .ThenBy(item => item.SubtitlePath)
                .ToList();
            result.FilesWithDrawings = result.FlaggedItems.Count;

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

    private async Task<List<(string Path, AssArtifactScanResult Scan)>> ScanGeneratedOutputArtifactsAsync(
        TranslationRequest translation,
        HashSet<string> scannedPaths,
        AssVerificationResult result)
    {
        var scans = new List<(string Path, AssArtifactScanResult Scan)>();
        foreach (var path in GetGeneratedOutputPaths(translation))
        {
            if (string.Equals(path, translation.TranslatedSubtitle, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path))
            {
                continue;
            }

            if (scannedPaths.Add(path))
            {
                result.TotalFilesScanned++;
            }

            var scan = await GetSuspiciousLines(path);
            if (scan.HasIssues)
            {
                scans.Add((path, scan));
            }
        }

        return scans;
    }

    private static List<string> GetGeneratedOutputPaths(TranslationRequest translation)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(translation.TranslatedSubtitle))
        {
            paths.Add(translation.TranslatedSubtitle);
        }

        if (!string.IsNullOrWhiteSpace(translation.GeneratedSubtitlePaths))
        {
            try
            {
                paths.AddRange(JsonSerializer.Deserialize<List<string>>(translation.GeneratedSubtitlePaths) ?? []);
            }
            catch (JsonException)
            {
                paths.Add(translation.GeneratedSubtitlePaths);
            }
        }

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool ShouldAttemptLocalRepair(AssArtifactScanResult scan)
    {
        return scan.IssueTypes.Contains(AssVerificationIssueTypes.InlineAssTagPlacement, StringComparer.Ordinal) ||
               scan.IssueTypes.Contains(AssVerificationIssueTypes.DrawingArtifact, StringComparer.Ordinal);
    }

    private async Task<SubtitleOutputBackfillResult> TryRepairExistingAssOutputsAsync(
        TranslationRequest translation,
        string subtitleTag,
        string subtitleTagShort)
    {
        if (!translation.MediaId.HasValue)
        {
            return new SubtitleOutputBackfillResult();
        }

        var repairContext = await LoadRepairContextAsync(translation);
        if (repairContext == null)
        {
            return new SubtitleOutputBackfillResult { BackfillSkippedFiles = 1, RequiresRetranslation = true };
        }

        return await _subtitleOutputBackfillService.RepairExistingAssOutputsAsync(
            repairContext.Value.Media,
            translation.MediaType,
            translation,
            repairContext.Value.MatchingSubtitles,
            subtitleTag,
            subtitleTagShort);
    }

    private async Task<(IMedia Media, List<Subtitles> MatchingSubtitles)?> LoadRepairContextAsync(
        TranslationRequest translation)
    {
        IMedia? media = translation.MediaType switch
        {
            MediaType.Movie => await _dbContext.Movies.FirstOrDefaultAsync(movie => movie.Id == translation.MediaId!.Value),
            MediaType.Episode => await _dbContext.Episodes
                .Include(episode => episode.Season)
                .ThenInclude(season => season.Show)
                .FirstOrDefaultAsync(episode => episode.Id == translation.MediaId!.Value),
            _ => null
        };

        if (media == null ||
            string.IsNullOrWhiteSpace(media.Path) ||
            string.IsNullOrWhiteSpace(media.FileName))
        {
            return null;
        }

        var subtitles = await _subtitleService.GetAllSubtitles(media.Path);
        var matchingSubtitles = FilterMatchingSubtitles(media.FileName, subtitles);
        return (media, matchingSubtitles);
    }

    private static List<Subtitles> FilterMatchingSubtitles(string mediaFileName, IEnumerable<Subtitles> subtitles)
    {
        var mediaNameNoExt = Path.GetFileNameWithoutExtension(mediaFileName);
        return subtitles
            .Where(subtitle =>
                subtitle.FileName.StartsWith(mediaFileName + ".", StringComparison.OrdinalIgnoreCase)
                || subtitle.FileName.Equals(mediaFileName, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(mediaNameNoExt)
                    && subtitle.FileName.StartsWith(mediaNameNoExt + ".", StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private async Task<AssArtifactScanResult> GetSuspiciousLines(string subtitlePath)
    {
        try
        {
            var lines = await File.ReadAllLinesAsync(subtitlePath);
            return AssSubtitleArtifactDetector.DetectDrawingArtifacts(lines);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading subtitle file {Path}", subtitlePath);
            return new AssArtifactScanResult();
        }
    }

    private static void AddOrMergeFinding(
        Dictionary<string, AssVerificationItem> flaggedByPath,
        int mediaId,
        string mediaType,
        string mediaTitle,
        string subtitlePath,
        bool isQueued,
        AssArtifactScanResult scan)
    {
        if (!flaggedByPath.TryGetValue(subtitlePath, out var item))
        {
            item = new AssVerificationItem
            {
                MediaId = mediaId,
                MediaType = mediaType,
                MediaTitle = mediaTitle,
                SubtitlePath = subtitlePath,
                Dismissed = false,
                IsQueued = isQueued
            };
            flaggedByPath[subtitlePath] = item;
        }

        item.SuspiciousLineCount += scan.SuspiciousLineCount;
        item.SuspiciousLines.AddRange(scan.SuspiciousLines);
        item.SuspiciousLines = item.SuspiciousLines
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .ToList();

        foreach (var issueType in scan.IssueTypes)
        {
            if (!item.IssueTypes.Contains(issueType, StringComparer.Ordinal))
            {
                item.IssueTypes.Add(issueType);
            }
        }

        var issueSummaries = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.IssueSummary))
        {
            issueSummaries.Add(item.IssueSummary);
        }

        issueSummaries.AddRange(scan.IssueSummaries);
        item.IssueSummary = string.Join(" ", issueSummaries.Distinct(StringComparer.Ordinal));
        item.IsQueued = item.IsQueued || isQueued;
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
    public string? StatusMessage { get; set; }

    public double ProgressPercent => Total > 0 ? (double)ProcessedCount / Total * 100 : 0;
}
