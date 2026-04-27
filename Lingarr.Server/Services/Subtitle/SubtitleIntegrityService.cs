using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.Subtitle;

/// <summary>
/// Service for validating subtitle integrity by comparing line counts
/// between source and target subtitles to detect partial/corrupted translations.
/// </summary>
public class SubtitleIntegrityService : ISubtitleIntegrityService
{
    private readonly ISettingService _settingService;
    private readonly ISubtitleService _subtitleService;
    private readonly LingarrDbContext _dbContext;
    private readonly ISourceSubtitleResolver _sourceSubtitleResolver;
    private readonly ILogger<SubtitleIntegrityService> _logger;

    /// <summary>
    /// Tolerance percentage for line count comparison.
    /// Target can have up to this percentage fewer lines than source and still be valid.
    /// </summary>
    private const double TolerancePercentage = 0.05; // 5%

    public SubtitleIntegrityService(
        ISettingService settingService,
        ISubtitleService subtitleService,
        LingarrDbContext dbContext,
        ISourceSubtitleResolver sourceSubtitleResolver,
        ILogger<SubtitleIntegrityService> logger)
    {
        _settingService = settingService;
        _subtitleService = subtitleService;
        _dbContext = dbContext;
        _sourceSubtitleResolver = sourceSubtitleResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> ValidateIntegrityAsync(string sourceSubtitlePath, string targetSubtitlePath)
    {
        // Check if integrity validation is enabled
        var enabled = await _settingService.GetSetting(SettingKeys.SubtitleValidation.IntegrityValidationEnabled);
        if (enabled != "true")
        {
            _logger.LogInformation("Integrity validation is disabled (setting={Setting}), skipping check for {TargetPath}", enabled ?? "null", targetSubtitlePath);
            return true; // Validation disabled, treat as valid
        }

        // Validate file existence
        if (!File.Exists(sourceSubtitlePath))
        {
            _logger.LogWarning("Source subtitle not found for integrity check: {Path}", sourceSubtitlePath);
            return true; // Can't validate without source
        }

        if (!File.Exists(targetSubtitlePath))
        {
            _logger.LogInformation("Target subtitle not found for integrity check: {Path}", targetSubtitlePath);
            return true; // No target to validate
        }

        try
        {
            // Parse both subtitle files
            var sourceSubtitles = await _subtitleService.ReadSubtitles(sourceSubtitlePath);
            var targetSubtitles = await _subtitleService.ReadSubtitles(targetSubtitlePath);

            var sourceCount = sourceSubtitles.Count;
            var targetCount = targetSubtitles.Count;

            if (sourceCount == 0)
            {
                _logger.LogInformation("Source subtitle has no lines, skipping integrity check");
                return true;
            }

            // Calculate minimum acceptable line count (with tolerance)
            var minimumAcceptable = (int)(sourceCount * (1 - TolerancePercentage));

            if (targetCount < minimumAcceptable)
            {
                _logger.LogWarning(
                    "Subtitle integrity check FAILED: Target has {TargetCount} lines but source has {SourceCount} (minimum acceptable: {Minimum}). " +
                    "File may be corrupted/partial: {TargetPath}",
                    targetCount, sourceCount, minimumAcceptable, targetSubtitlePath);
                return false;
            }

            _logger.LogInformation(
                "Subtitle integrity check PASSED: {TargetCount}/{SourceCount} lines in {Path}",
                targetCount, sourceCount, targetSubtitlePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during subtitle integrity check for {TargetPath}", targetSubtitlePath);
            // On error, don't block processing - return true
            return true;
        }
    }

    /// <inheritdoc />
    public async Task<Models.AssVerificationResult> VerifyAssIntegrityAsync(CancellationToken ct)
    {
        var result = new Models.AssVerificationResult();
        var flaggedByPath = new Dictionary<string, Models.AssVerificationItem>(StringComparer.OrdinalIgnoreCase);
        var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Get all pending/in-progress translation requests to check if media is already queued
        var queuedRequests = await _dbContext.Set<Lingarr.Core.Entities.TranslationRequest>()
            .Where(tr => tr.Status == Core.Enum.TranslationStatus.Pending || 
                         tr.Status == Core.Enum.TranslationStatus.InProgress)
            .Where(tr => tr.MediaId != null)
            .Select(tr => new { tr.MediaId, tr.MediaType })
            .ToListAsync(ct);
        
        // Build lookup sets for quick checking
        var queuedMovieIds = queuedRequests
            .Where(r => r.MediaType == Core.Enum.MediaType.Movie)
            .Select(r => r.MediaId!.Value)
            .ToHashSet();
        
        var queuedEpisodeIds = queuedRequests
            .Where(r => r.MediaType == Core.Enum.MediaType.Episode)
            .Select(r => r.MediaId!.Value)
            .ToHashSet();

        var completedTranslations = await _dbContext.Set<Lingarr.Core.Entities.TranslationRequest>()
            .Where(tr => tr.Status == TranslationStatus.Completed)
            .Where(tr => tr.MediaId != null)
            .Where(tr => tr.SubtitleToTranslate != null && tr.TranslatedSubtitle != null)
            .ToListAsync(ct);

        // Get all movies and episodes with their subtitle paths
        var movies = await _dbContext.Movies
            .Where(m => m.Path != null)
            .Select(m => new { m.Id, m.Title, m.Path, m.FileName })
            .ToListAsync(ct);

        var episodes = await _dbContext.Episodes
            .Include(e => e.Season)
            .ThenInclude(s => s.Show)
            .Where(e => e.Path != null)
            .Select(e => new { 
                e.Id, 
                Title = $"{e.Season.Show.Title} S{e.Season.SeasonNumber:D2}E{e.EpisodeNumber:D2}",
                e.Path, 
                e.FileName 
            })
            .ToListAsync(ct);

        // Process movies
        foreach (var movie in movies)
        {
            if (ct.IsCancellationRequested) break;
            
            var subtitleFiles = await GetTranslatedSubtitlesForMedia(movie.Path!, movie.FileName!);
            foreach (var subPath in subtitleFiles)
            {
                if (scannedPaths.Add(subPath))
                {
                    result.TotalFilesScanned++;
                }

                var scan = await GetSuspiciousLines(subPath, ct);
                
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

        // Process episodes
        foreach (var episode in episodes)
        {
            if (ct.IsCancellationRequested) break;
            
            var subtitleFiles = await GetTranslatedSubtitlesForMedia(episode.Path!, episode.FileName!);
            foreach (var subPath in subtitleFiles)
            {
                if (scannedPaths.Add(subPath))
                {
                    result.TotalFilesScanned++;
                }

                var scan = await GetSuspiciousLines(subPath, ct);
                
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

        foreach (var translation in completedTranslations)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                if (translation.MediaId == null ||
                    string.IsNullOrWhiteSpace(translation.SubtitleToTranslate) ||
                    string.IsNullOrWhiteSpace(translation.TranslatedSubtitle) ||
                    !File.Exists(translation.TranslatedSubtitle))
                {
                    continue;
                }

                var sourceSubtitlePath = await _sourceSubtitleResolver.ResolveReadableSourcePathAsync(
                    translation,
                    ct);
                if (string.IsNullOrWhiteSpace(sourceSubtitlePath) || !File.Exists(sourceSubtitlePath))
                {
                    continue;
                }

                if (scannedPaths.Add(translation.TranslatedSubtitle))
                {
                    result.TotalFilesScanned++;
                }

                var sourceSubtitles = await _subtitleService.ReadSubtitles(sourceSubtitlePath);
                var targetSubtitles = await _subtitleService.ReadSubtitles(translation.TranslatedSubtitle);
                var scan = AssSubtitleArtifactDetector.CompareTagStructure(
                    sourceSubtitles,
                    targetSubtitles,
                    translation.TranslatedSubtitle);

                if (!scan.HasIssues)
                {
                    continue;
                }

                var isQueued = translation.MediaType == MediaType.Movie
                    ? queuedMovieIds.Contains(translation.MediaId.Value)
                    : queuedEpisodeIds.Contains(translation.MediaId.Value);

                AddOrMergeFinding(
                    flaggedByPath,
                    translation.MediaId.Value,
                    translation.MediaType.ToString(),
                    translation.Title,
                    translation.TranslatedSubtitle,
                    isQueued,
                    scan);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Error comparing ASS tag structure for translation target {Path}",
                    translation.TranslatedSubtitle);
            }
        }

        result.FlaggedItems = flaggedByPath.Values
            .OrderBy(item => item.MediaTitle)
            .ThenBy(item => item.SubtitlePath)
            .ToList();
        result.FilesWithDrawings = result.FlaggedItems.Count;

        _logger.LogInformation(
            "ASS Verification complete: Scanned {Total} files, found {Flagged} with ASS/SSA artifacts",
            result.TotalFilesScanned, result.FilesWithDrawings);

        return result;
    }

    private async Task<List<string>> GetTranslatedSubtitlesForMedia(string mediaPath, string mediaFileName)
    {
        var subtitleFiles = new List<string>();
        try
        {
            var allSubs = await _subtitleService.GetAllSubtitles(mediaPath);
            // Filter to only subtitles for this specific media file
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

    private async Task<AssArtifactScanResult> GetSuspiciousLines(string subtitlePath, CancellationToken ct)
    {
        try
        {
            var lines = await File.ReadAllLinesAsync(subtitlePath, ct);
            return AssSubtitleArtifactDetector.DetectDrawingArtifacts(lines);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading subtitle file {Path}", subtitlePath);
            return new AssArtifactScanResult();
        }
    }

    private static void AddOrMergeFinding(
        Dictionary<string, Models.AssVerificationItem> flaggedByPath,
        int mediaId,
        string mediaType,
        string mediaTitle,
        string subtitlePath,
        bool isQueued,
        AssArtifactScanResult scan)
    {
        if (!flaggedByPath.TryGetValue(subtitlePath, out var item))
        {
            item = new Models.AssVerificationItem
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

    /// <inheritdoc />
    public async Task<SubtitleTypeCheckResult?> ValidateSubtitleTypeAsync(int translationId, CancellationToken ct = default)
    {
        // Minimum entry count to consider a subtitle "complete" (not Forced/Signs-only)
        var minEntryThreshold = SubtitleExtractionService.MinimumDialogueEntries;

        // Get the translation request
        var translationRequest = await _dbContext.Set<Core.Entities.TranslationRequest>()
            .FirstOrDefaultAsync(tr => tr.Id == translationId, ct);

        if (translationRequest == null)
        {
            _logger.LogWarning("Translation request {TranslationId} not found for subtitle type validation", translationId);
            return null;
        }

        // Get the source subtitle path
        var sourceSubtitlePath = await _sourceSubtitleResolver.ResolveReadableSourcePathAsync(
            translationRequest,
            ct);
        if (string.IsNullOrEmpty(sourceSubtitlePath) || !File.Exists(sourceSubtitlePath))
        {
            _logger.LogWarning("Source subtitle not found for translation {TranslationId}: {Path}",
                translationId, sourceSubtitlePath ?? "null");
            return null;
        }

        try
        {
            // Parse the subtitle file and count entries
            var subtitles = await _subtitleService.ReadSubtitles(sourceSubtitlePath);
            var entryCount = subtitles.Count;

            // Determine if subtitle is complete
            var isComplete = entryCount >= minEntryThreshold;

            // Get media info for the result
            string mediaTitle;
            int mediaId;
            string mediaType;

            if (translationRequest.MediaType == MediaType.Movie && translationRequest.MediaId.HasValue)
            {
                var movie = await _dbContext.Movies
                    .FirstOrDefaultAsync(m => m.Id == translationRequest.MediaId.Value, ct);
                mediaTitle = movie?.Title ?? translationRequest.Title;
                mediaId = movie?.Id ?? 0;
                mediaType = "Movie";
            }
            else if (translationRequest.MediaType == MediaType.Episode && translationRequest.MediaId.HasValue)
            {
                var episode = await _dbContext.Episodes
                    .Include(e => e.Season)
                    .ThenInclude(s => s.Show)
                    .FirstOrDefaultAsync(e => e.Id == translationRequest.MediaId.Value, ct);
                mediaTitle = episode != null
                    ? $"{episode.Season.Show.Title} S{episode.Season.SeasonNumber:D2}E{episode.EpisodeNumber:D2}"
                    : translationRequest.Title;
                mediaId = episode?.Id ?? 0;
                mediaType = "Episode";
            }
            else
            {
                mediaTitle = translationRequest.Title;
                mediaId = 0;
                mediaType = translationRequest.MediaType.ToString();
            }

            // Check if already queued
            var isQueued = await _dbContext.Set<Core.Entities.TranslationRequest>()
                .AnyAsync(tr => tr.MediaId == mediaId
                    && tr.MediaType == translationRequest.MediaType
                    && (tr.Status == TranslationStatus.Pending || tr.Status == TranslationStatus.InProgress), ct);

            var result = new SubtitleTypeCheckResult
            {
                TranslationId = translationId,
                MediaTitle = mediaTitle,
                SubtitlePath = sourceSubtitlePath,
                EntryCount = entryCount,
                IsComplete = isComplete,
                MediaType = mediaType,
                MediaId = mediaId,
                IsQueued = isQueued,
                Dismissed = false
            };

            if (!isComplete)
            {
                result.Warning = $"Only {entryCount} entries - likely Forced or Signs-only subtitle";
                result.RecommendedAction = "Re-translate with different subtitle";
                
                _logger.LogWarning(
                    "Subtitle type check: {MediaTitle} has only {EntryCount} entries - likely incomplete subtitle (Forced/Signs). " +
                    "Source: {Path}",
                    mediaTitle, entryCount, sourceSubtitlePath);
            }
            else
            {
                _logger.LogDebug(
                    "Subtitle type check passed: {MediaTitle} has {EntryCount} entries",
                    mediaTitle, entryCount);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during subtitle type validation for translation {TranslationId}", translationId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<SubtitleTypeCheckSummary> ValidateAllSubtitleTypesAsync(CancellationToken ct)
    {
        var result = new SubtitleTypeCheckSummary();
        
        // Get all completed translation requests
        var completedTranslations = await _dbContext.Set<Core.Entities.TranslationRequest>()
            .Where(tr => tr.Status == TranslationStatus.Completed)
            .Where(tr => tr.MediaId != null)
            .ToListAsync(ct);

        _logger.LogInformation("Starting subtitle type validation for {Count} completed translations",
            completedTranslations.Count);

        foreach (var translation in completedTranslations)
        {
            if (ct.IsCancellationRequested) break;

            result.TotalScanned++;

            var checkResult = await ValidateSubtitleTypeAsync(translation.Id, ct);
            
            if (checkResult != null && !checkResult.IsComplete)
            {
                result.IncompleteCount++;
                result.FlaggedItems.Add(checkResult);
            }
        }

        _logger.LogInformation(
            "Subtitle type validation complete: Scanned {Total}, Found {Incomplete} incomplete subtitles",
            result.TotalScanned, result.IncompleteCount);

        return result;
    }
}
