using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
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
        ILogger<SubtitleIntegrityService> logger)
    {
        _settingService = settingService;
        _subtitleService = subtitleService;
        _dbContext = dbContext;
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
        
        // Pattern to detect ASS drawing commands: lines starting with "m <number> <number>"
        var drawingPattern = new System.Text.RegularExpressions.Regex(
            @"^\s*m\s+-?\d+(\.\d+)?\s+-?\d+(\.\d+)?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Minimum suspicious lines to flag a file
        const int suspiciousThreshold = 2;

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
                result.TotalFilesScanned++;
                var (count, lines) = await GetSuspiciousLines(subPath, drawingPattern);
                
                if (count >= suspiciousThreshold)
                {
                    result.FilesWithDrawings++;
                    result.FlaggedItems.Add(new Models.AssVerificationItem
                    {
                        MediaId = movie.Id,
                        MediaType = "Movie",
                        MediaTitle = movie.Title ?? "Unknown",
                        SubtitlePath = subPath,
                        SuspiciousLineCount = count,
                        SuspiciousLines = lines,
                        Dismissed = false
                    });
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
                result.TotalFilesScanned++;
                var (count, lines) = await GetSuspiciousLines(subPath, drawingPattern);
                
                if (count >= suspiciousThreshold)
                {
                    result.FilesWithDrawings++;
                    result.FlaggedItems.Add(new Models.AssVerificationItem
                    {
                        MediaId = episode.Id,
                        MediaType = "Episode",
                        MediaTitle = episode.Title,
                        SubtitlePath = subPath,
                        SuspiciousLineCount = count,
                        SuspiciousLines = lines,
                        Dismissed = false
                    });
                }
            }
        }

        _logger.LogInformation(
            "ASS Verification complete: Scanned {Total} files, found {Flagged} with drawing artifacts",
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

    private async Task<(int count, List<string> lines)> GetSuspiciousLines(string subtitlePath, System.Text.RegularExpressions.Regex pattern)
    {
        try
        {
            var lines = await File.ReadAllLinesAsync(subtitlePath);
            var suspiciousLines = lines
                .Where(line => pattern.IsMatch(line.Trim()))
                .Take(10) // Limit to first 10 for performance
                .Select(line => line.Trim().Length > 80 ? line.Trim().Substring(0, 80) + "..." : line.Trim())
                .ToList();
            return (suspiciousLines.Count, suspiciousLines);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading subtitle file {Path}", subtitlePath);
            return (0, new List<string>());
        }
    }
}
