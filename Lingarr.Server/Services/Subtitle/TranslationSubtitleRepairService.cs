using System.Text.Json;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.Subtitle;

public class TranslationSubtitleRepairService : ITranslationSubtitleRepairService
{
    private readonly LingarrDbContext _dbContext;
    private readonly ISubtitleService _subtitleService;
    private readonly ISettingService _settingService;
    private readonly ISubtitleExtractionService _extractionService;
    private readonly ILogger<TranslationSubtitleRepairService> _logger;

    public TranslationSubtitleRepairService(
        LingarrDbContext dbContext,
        ISubtitleService subtitleService,
        ISettingService settingService,
        ISubtitleExtractionService extractionService,
        ILogger<TranslationSubtitleRepairService> logger)
    {
        _dbContext = dbContext;
        _subtitleService = subtitleService;
        _settingService = settingService;
        _extractionService = extractionService;
        _logger = logger;
    }

    public async Task<SubtitleRepairSummary> RepairOrphanedRecordsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new SubtitleRepairSummary();

        var orphanedRequests = await _dbContext.TranslationRequests
            .Where(r => r.Status == TranslationStatus.Completed
                && r.MediaId != null
                && (r.TranslatedSubtitle == null
                    || r.TranslatedSubtitle == ""))
            .OrderByDescending(r => r.CompletedAt)
            .ToListAsync(cancellationToken);

        result.Scanned = orphanedRequests.Count;
        if (orphanedRequests.Count == 0)
        {
            _logger.LogInformation("No orphaned translation records found to repair");
            return result;
        }

        _logger.LogInformation(
            "Found {Count} orphaned translation records to repair",
            orphanedRequests.Count);

        var settings = await _settingService.GetSettings([
            SettingKeys.Translation.UseSubtitleTagging,
            SettingKeys.Translation.RemoveLanguageTag,
            SettingKeys.Translation.SubtitleTag,
            SettingKeys.Translation.SubtitleTagShort
        ]);

        var useSubtitleTagging =
            settings.TryGetValue(SettingKeys.Translation.UseSubtitleTagging, out var useTaggingValue) &&
            string.Equals(useTaggingValue, "true", StringComparison.OrdinalIgnoreCase);
        var removeLanguageTag =
            settings.TryGetValue(SettingKeys.Translation.RemoveLanguageTag, out var removeLanguageTagValue) &&
            string.Equals(removeLanguageTagValue, "true", StringComparison.OrdinalIgnoreCase);
        var configuredTag = useSubtitleTagging
            ? settings.GetValueOrDefault(SettingKeys.Translation.SubtitleTag) ?? string.Empty
            : string.Empty;
        var configuredShortTag = useSubtitleTagging
            ? settings.GetValueOrDefault(SettingKeys.Translation.SubtitleTagShort) ?? string.Empty
            : string.Empty;

        foreach (var request in orphanedRequests)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var mediaPath = await ResolveMediaFilePathAsync(request, cancellationToken);
                if (mediaPath == null)
                {
                    result.SkippedNoMediaPath++;
                    result.Details.Add($"Request {request.Id} ({request.Title}): no media file path resolved");
                    continue;
                }

                if (await TryRepairFromGeneratedPathsAsync(request, result, cancellationToken))
                {
                    continue;
                }

                if (await TryRepairFromFallbackPathsAsync(
                        request, mediaPath, configuredTag, configuredShortTag,
                        removeLanguageTag, result, cancellationToken))
                {
                    continue;
                }

                if (await TryRepairWithMkvMarkerAsync(
                        request, mediaPath, result, cancellationToken))
                {
                    continue;
                }

                result.Unfixable++;
                result.Details.Add($"Request {request.Id} ({request.Title}): no translation files found on disk");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error repairing translation request {RequestId}", request.Id);
                result.Unfixable++;
                result.Details.Add($"Request {request.Id}: error - {ex.Message}");
            }
        }

        var fixedCount = result.FixedByExistingFiles + result.FixedByMkvMarker;
        if (fixedCount > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Repaired {Fixed} orphaned translation records ({MkvMarker} via MKV marker)",
                fixedCount,
                result.FixedByMkvMarker);
        }

        return result;
    }

    private async Task<string?> ResolveMediaFilePathAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.MediaId.HasValue)
        {
            return null;
        }

        if (request.MediaType == MediaType.Movie)
        {
            var movie = await _dbContext.Movies
                .FirstOrDefaultAsync(m => m.Id == request.MediaId.Value, cancellationToken);
            if (movie == null || string.IsNullOrWhiteSpace(movie.Path) || string.IsNullOrWhiteSpace(movie.FileName))
            {
                return null;
            }
            return Path.Combine(movie.Path, movie.FileName);
        }

        if (request.MediaType == MediaType.Episode)
        {
            var episode = await _dbContext.Episodes
                .FirstOrDefaultAsync(e => e.Id == request.MediaId.Value, cancellationToken);
            if (episode == null || string.IsNullOrWhiteSpace(episode.Path) || string.IsNullOrWhiteSpace(episode.FileName))
            {
                return null;
            }
            return Path.Combine(episode.Path, episode.FileName);
        }

        return null;
    }

    private async Task<bool> TryRepairFromGeneratedPathsAsync(
        TranslationRequest request,
        SubtitleRepairSummary result,
        CancellationToken cancellationToken)
    {
        var paths = DeserializeGeneratedPaths(request.GeneratedSubtitlePaths);
        if (paths.Count == 0)
        {
            return false;
        }

        var existingPaths = paths.Where(File.Exists).ToList();
        if (existingPaths.Count == 0)
        {
            return false;
        }

        request.TranslatedSubtitle = existingPaths[0];
        _logger.LogInformation(
            "Repaired request {RequestId}: found existing file at {Path}",
            request.Id,
            existingPaths[0]);
        result.FixedByExistingFiles++;
        result.Details.Add($"Request {request.Id}: fixed via generated path {existingPaths[0]}");
        return true;
    }

    private async Task<bool> TryRepairFromFallbackPathsAsync(
        TranslationRequest request,
        string mediaPath,
        string subtitleTag,
        string subtitleTagShort,
        bool removeLanguageTag,
        SubtitleRepairSummary result,
        CancellationToken cancellationToken)
    {
        var targetLanguageOptions = new[]
        {
            removeLanguageTag ? string.Empty : request.TargetLanguage,
            request.TargetLanguage,
            string.Empty
        };

        var tagPairs = new (string Tag, string ShortTag)[]
        {
            (subtitleTag, subtitleTagShort),
            ("[Lingarr]", "-ai-"),
            (string.Empty, string.Empty)
        };

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var targetLanguage in targetLanguageOptions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var (tag, shortTag) in tagPairs.Distinct())
            {
                foreach (var candidatePath in _subtitleService.CreateFallbackPaths(
                             mediaPath,
                             targetLanguage,
                             tag,
                             shortTag))
                {
                    candidates.Add(candidatePath);
                }
            }
        }

        var existingPath = candidates.FirstOrDefault(File.Exists);
        if (existingPath == null)
        {
            return false;
        }

        request.TranslatedSubtitle = existingPath;
        UpdateGeneratedPaths(request, [existingPath]);
        _logger.LogInformation(
            "Repaired request {RequestId}: resolved via fallback path {Path}",
            request.Id,
            existingPath);
        result.FixedByExistingFiles++;
        result.Details.Add($"Request {request.Id}: fixed via fallback path {existingPath}");
        return true;
    }

    private async Task<bool> TryRepairWithMkvMarkerAsync(
        TranslationRequest request,
        string mediaPath,
        SubtitleRepairSummary result,
        CancellationToken cancellationToken)
    {
        var mediaDir = Path.GetDirectoryName(mediaPath);
        if (string.IsNullOrWhiteSpace(mediaDir))
        {
            return false;
        }

        var mergedFiles = Directory.GetFiles(mediaDir, "lingarr_merged_*");
        if (mergedFiles.Length == 0)
        {
            return false;
        }

        var mkvFile = mergedFiles
            .Where(f => f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => new FileInfo(f).CreationTime)
            .FirstOrDefault();

        if (mkvFile == null)
        {
            return false;
        }

        var embeddedSubtitles = await _extractionService.ProbeEmbeddedSubtitles(mkvFile);
        if (embeddedSubtitles.Count == 0)
        {
            return false;
        }

        var targetLanguage = SubtitleLanguageHelper.NormalizeLanguageCode(request.TargetLanguage);

        var lingarrSubtitles = embeddedSubtitles
            .Where(s => s.IsTextBased &&
                        s.Title?.Contains("Lingarr", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        var selected = lingarrSubtitles.FirstOrDefault(s =>
            SubtitleLanguageHelper.LanguageMatches(s.Language, targetLanguage));

        selected ??= lingarrSubtitles.FirstOrDefault();

        if (selected == null)
        {
            return false;
        }

        var marker = $"mkv-embedded:stream{selected.StreamIndex}|{mkvFile}";
        request.TranslatedSubtitle = marker;
        _logger.LogInformation(
            "Repaired request {RequestId}: set MKV-embedded marker for {MkvFile}",
            request.Id,
            mkvFile);
        result.FixedByMkvMarker++;
        result.Details.Add($"Request {request.Id}: fixed via MKV-embedded marker {mkvFile}");
        return true;
    }

    private static List<string> DeserializeGeneratedPaths(string? generatedPathsJson)
    {
        if (string.IsNullOrWhiteSpace(generatedPathsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(generatedPathsJson) ?? [];
        }
        catch
        {
            return generatedPathsJson.Split(
                '|',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
    }

    private static void UpdateGeneratedPaths(TranslationRequest request, List<string> paths)
    {
        var existing = DeserializeGeneratedPaths(request.GeneratedSubtitlePaths);
        foreach (var path in paths)
        {
            if (!existing.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                existing.Add(path);
            }
        }
        request.GeneratedSubtitlePaths = JsonSerializer.Serialize(existing);
    }
}
