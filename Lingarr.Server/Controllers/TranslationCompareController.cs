using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Controllers;

[ApiController]
[Route("api/translation-compare")]
public class TranslationCompareController : ControllerBase
{
    private const string TranslatorInfoPrefix = "# Translated with Lingarr using";

    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settingService;
    private readonly ISubtitleExtractionService _extractionService;
    private readonly ISourceSubtitleResolver _sourceSubtitleResolver;
    private readonly ISubtitleService _subtitleService;
    private readonly ILogger<TranslationCompareController> _logger;
    private string? _tempTranslatedComparePath;

    public TranslationCompareController(
        LingarrDbContext dbContext,
        ISettingService settingService,
        ISubtitleExtractionService extractionService,
        ISourceSubtitleResolver sourceSubtitleResolver,
        ISubtitleService subtitleService,
        ILogger<TranslationCompareController> logger)
    {
        _dbContext = dbContext;
        _settingService = settingService;
        _extractionService = extractionService;
        _sourceSubtitleResolver = sourceSubtitleResolver;
        _subtitleService = subtitleService;
        _logger = logger;
    }

    [HttpGet("{requestId:int}")]
    public async Task<ActionResult<CompletedTranslationCompareResponse>> GetCompletedTranslationCompare(
        int requestId,
        CancellationToken cancellationToken = default)
    {
        var request = await _dbContext.TranslationRequests
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

        if (request == null)
        {
            return NotFound(new { message = $"Translation request {requestId} was not found." });
        }

        if (request.Status != TranslationStatus.Completed)
        {
            return BadRequest(new
            {
                message =
                    $"Translation request {requestId} is not completed. Current status: {request.Status}."
            });
        }

        if (string.IsNullOrWhiteSpace(request.SubtitleToTranslate))
        {
            return NotFound(new
            {
                message =
                    $"Translation request {requestId} does not contain a source subtitle path."
            });
        }

        ResolvedSubtitlePath? originalSubtitle = null;

        try
        {
            originalSubtitle = await ResolveSourceSubtitlePathAsync(request, cancellationToken);
            if (originalSubtitle == null)
            {
                return NotFound(new
                {
                    message =
                        $"Source subtitle file does not exist on disk and no temporary source subtitle could be recovered: {request.SubtitleToTranslate}"
                });
            }

            if (!System.IO.File.Exists(originalSubtitle.Path))
            {
                return NotFound(new
                {
                    message =
                        $"Source subtitle file does not exist on disk: {originalSubtitle.Path}"
                });
            }

            var translatedSubtitlePath =
                await ResolveTranslatedSubtitlePathAsync(request, cancellationToken);

            if (string.IsNullOrWhiteSpace(translatedSubtitlePath))
            {
                return NotFound(new
                {
                    message =
                        $"Translation request {requestId} does not contain a translated subtitle path, and no translated subtitle file could be resolved on disk."
                });
            }

            if (!System.IO.File.Exists(translatedSubtitlePath))
            {
                return NotFound(new
                {
                    message =
                        $"Translated subtitle file does not exist on disk: {translatedSubtitlePath}"
                });
            }

            var originalSubtitles = await _subtitleService.ReadSubtitles(originalSubtitle.Path);
            var translatedSubtitles = await _subtitleService.ReadSubtitles(translatedSubtitlePath);
            var filteredTranslatedSubtitles = RemoveTranslatorInfoLines(translatedSubtitles);
            var lines = BuildLineComparison(originalSubtitles, filteredTranslatedSubtitles);

            var response = new CompletedTranslationCompareResponse
            {
                TranslationRequestId = request.Id,
                Title = request.Title,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                MediaType = request.MediaType.ToString(),
                CompletedAt = request.CompletedAt,
                OriginalSubtitlePath = originalSubtitle.Path,
                TranslatedSubtitlePath = translatedSubtitlePath,
                OriginalLineCount = originalSubtitles.Count,
                TranslatedLineCount = filteredTranslatedSubtitles.Count,
                Lines = lines
            };

            if (ControllerContext.HttpContext != null)
            {
                Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
                Response.Headers.Pragma = "no-cache";
                Response.Headers.Expires = "0";
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build compare payload for translation request {RequestId}", requestId);
            return StatusCode(500, new { message = "Failed to load subtitle compare data." });
        }
    finally
    {
        CleanupTemporarySubtitle(originalSubtitle);

        if (!string.IsNullOrWhiteSpace(_tempTranslatedComparePath) &&
            System.IO.File.Exists(_tempTranslatedComparePath))
        {
            try
            {
                System.IO.File.Delete(_tempTranslatedComparePath);
                _tempTranslatedComparePath = null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete temporary translated compare subtitle: {Path}", _tempTranslatedComparePath);
            }
        }
    }
    }

    private async Task<string?> ResolveTranslatedSubtitlePathAsync(
        Core.Entities.TranslationRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.TranslatedSubtitle) &&
            System.IO.File.Exists(request.TranslatedSubtitle))
        {
            return request.TranslatedSubtitle;
        }
        // Check for mkv-embedded marker — the subtitle was embedded into the MKV
        // container rather than written as a standalone file
        if (request.TranslatedSubtitle?.StartsWith("mkv-embedded:", StringComparison.OrdinalIgnoreCase) == true)
        {
            return await ExtractTranslatedSubtitleFromMkvAsync(
                request.TranslatedSubtitle, request, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(request.SubtitleToTranslate))
        {
            return null;
        }

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

        var candidatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(request.TranslatedSubtitle))
        {
            candidatePaths.Add(request.TranslatedSubtitle);
        }

        var tagPairs = new (string Tag, string ShortTag)[]
        {
            (configuredTag, configuredShortTag),
            ("[Lingarr]", "-ai-"),
            (string.Empty, string.Empty)
        };

        var targetLanguageOptions = new[]
        {
            removeLanguageTag ? string.Empty : request.TargetLanguage,
            request.TargetLanguage,
            string.Empty
        };

        foreach (var targetLanguage in targetLanguageOptions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var (tag, shortTag) in tagPairs.Distinct())
            {
                foreach (var candidatePath in _subtitleService.CreateFallbackPaths(
                             request.SubtitleToTranslate,
                             targetLanguage,
                             tag,
                             shortTag))
                {
                    candidatePaths.Add(candidatePath);
                }
            }
        }

        var resolvedPath = candidatePaths.FirstOrDefault(System.IO.File.Exists);
        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            return null;
        }

        if (!string.Equals(request.TranslatedSubtitle, resolvedPath, StringComparison.OrdinalIgnoreCase))
        {
            request.TranslatedSubtitle = resolvedPath;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return resolvedPath;
    }
    /// <summary>
    /// Extracts a translated subtitle that was embedded in an MKV container to a temporary file.
    /// Used when the translated subtitle path is an mkv-embedded: marker rather than a real file path.
    /// </summary>
    private async Task<string?> ExtractTranslatedSubtitleFromMkvAsync(
        string translatedSubtitle,
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        // Parse "mkv-embedded:streamN|MKV_PATH" to extract the MKV file path
        var pipeIndex = translatedSubtitle.IndexOf('|', StringComparison.Ordinal);
        if (pipeIndex < 0 || pipeIndex >= translatedSubtitle.Length - 1)
        {
            return null;
        }

        var mkvPath = translatedSubtitle[(pipeIndex + 1)..];
        if (!System.IO.File.Exists(mkvPath))
        {
            _logger.LogWarning(
                "MKV file for embedded translated subtitle not found: {MkvPath}", mkvPath);
            return null;
        }

        // Probe to find the Lingarr track (don't rely on the stream index in the marker —
        // the actual FFprobe stream index may differ due to other embedded subtitle tracks)
        var embeddedSubtitles = await _extractionService.ProbeEmbeddedSubtitles(mkvPath);
        if (embeddedSubtitles.Count == 0)
        {
            _logger.LogWarning(
                "No embedded subtitles found in MKV: {MkvPath}", mkvPath);
            return null;
        }

        var targetLanguage = SubtitleLanguageHelper.NormalizeLanguageCode(request.TargetLanguage);

        // Priority 1: stream whose title contains "Lingarr" AND language matches target
        var selected = embeddedSubtitles.FirstOrDefault(s =>
            s.Title?.Contains("Lingarr", StringComparison.OrdinalIgnoreCase) == true &&
            SubtitleLanguageHelper.LanguageMatches(s.Language, targetLanguage));

        // Priority 2: any text-based stream whose language matches target
        selected ??= embeddedSubtitles.FirstOrDefault(s =>
            s.IsTextBased &&
            SubtitleLanguageHelper.LanguageMatches(s.Language, targetLanguage));

        // Priority 3: any text-based stream containing "Lingarr" in the title
        selected ??= embeddedSubtitles.FirstOrDefault(s =>
            s.IsTextBased &&
            s.Title?.Contains("Lingarr", StringComparison.OrdinalIgnoreCase) == true);

        if (selected == null)
        {
            _logger.LogWarning(
                "No matching Lingarr translated subtitle stream found in MKV: {MkvPath} (target language: {TargetLanguage})",
                mkvPath,
                request.TargetLanguage);
            return null;
        }

        // Extract to a temporary directory for compare
        var tempDir = Path.Combine(Path.GetTempPath(), "lingarr_translated_compare");
        var extractedPath = await _extractionService.ExtractSubtitle(
            mkvPath,
            selected.StreamIndex,
            tempDir,
            selected.CodecName,
            targetLanguage);

        if (string.IsNullOrWhiteSpace(extractedPath) || !System.IO.File.Exists(extractedPath))
        {
            _logger.LogWarning(
                "Failed to extract translated subtitle from MKV: {MkvPath} (stream {StreamIndex})",
                mkvPath,
                selected.StreamIndex);
            return null;
        }

        _tempTranslatedComparePath = extractedPath;
        _logger.LogInformation(
            "Extracted embedded translated subtitle for compare: {Path} (stream {StreamIndex})",
            extractedPath,
            selected.StreamIndex);
        return extractedPath;
    }


    private async Task<ResolvedSubtitlePath?> ResolveSourceSubtitlePathAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        var sourcePath = await _sourceSubtitleResolver.ResolveReadableSourcePathAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(sourcePath) || !System.IO.File.Exists(sourcePath))
        {
            return null;
        }

        return new ResolvedSubtitlePath(sourcePath, false);
    }

    private async Task<SourceExtractionCandidate?> GetSourceExtractionCandidateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.MediaId.HasValue)
        {
            return null;
        }

        List<EmbeddedSubtitle>? embeddedSubtitles = null;
        string? mediaPath = null;

        if (request.MediaType == MediaType.Movie)
        {
            var movie = await _dbContext.Movies
                .Include(m => m.EmbeddedSubtitles)
                .FirstOrDefaultAsync(m => m.Id == request.MediaId.Value, cancellationToken);

            if (movie == null || string.IsNullOrWhiteSpace(movie.Path) || string.IsNullOrWhiteSpace(movie.FileName))
            {
                return null;
            }

            if (movie.EmbeddedSubtitles == null || movie.EmbeddedSubtitles.Count == 0)
            {
                await _extractionService.SyncEmbeddedSubtitles(movie);
                await _dbContext.Entry(movie).Collection(m => m.EmbeddedSubtitles).LoadAsync(cancellationToken);
            }

            embeddedSubtitles = movie.EmbeddedSubtitles;
            mediaPath = Path.Combine(movie.Path, movie.FileName);
        }
        else if (request.MediaType == MediaType.Episode)
        {
            var episode = await _dbContext.Episodes
                .Include(e => e.EmbeddedSubtitles)
                .FirstOrDefaultAsync(e => e.Id == request.MediaId.Value, cancellationToken);

            if (episode == null || string.IsNullOrWhiteSpace(episode.Path) || string.IsNullOrWhiteSpace(episode.FileName))
            {
                return null;
            }

            if (episode.EmbeddedSubtitles == null || episode.EmbeddedSubtitles.Count == 0)
            {
                await _extractionService.SyncEmbeddedSubtitles(episode);
                await _dbContext.Entry(episode).Collection(e => e.EmbeddedSubtitles).LoadAsync(cancellationToken);
            }

            embeddedSubtitles = episode.EmbeddedSubtitles;
            mediaPath = Path.Combine(episode.Path, episode.FileName);
        }

        if (embeddedSubtitles == null || string.IsNullOrWhiteSpace(mediaPath))
        {
            return null;
        }

        var candidate = SelectBestSourceSubtitle(embeddedSubtitles, request);
        return candidate == null ? null : new SourceExtractionCandidate(mediaPath, candidate);
    }

    private static EmbeddedSubtitle? SelectBestSourceSubtitle(
        List<EmbeddedSubtitle> embeddedSubtitles,
        TranslationRequest request)
    {
        var textBased = embeddedSubtitles.Where(subtitle => subtitle.IsTextBased).ToList();
        if (textBased.Count == 0)
        {
            return null;
        }

        var exactPathMatch = textBased.FirstOrDefault(subtitle =>
            !string.IsNullOrWhiteSpace(subtitle.ExtractedPath) &&
            string.Equals(subtitle.ExtractedPath, request.SubtitleToTranslate, StringComparison.OrdinalIgnoreCase));
        if (exactPathMatch != null)
        {
            return exactPathMatch;
        }

        var matchingTitle = textBased
            .Where(subtitle => TitlesMatch(subtitle.Title, request.SelectedStreamTitle))
            .OrderByDescending(subtitle => ScoreSourceCandidate(subtitle, request))
            .FirstOrDefault();
        if (matchingTitle != null)
        {
            return matchingTitle;
        }

        return textBased
            .OrderByDescending(subtitle => ScoreSourceCandidate(subtitle, request))
            .FirstOrDefault();
    }

    private static int ScoreSourceCandidate(EmbeddedSubtitle subtitle, TranslationRequest request)
    {
        var score = SubtitleLanguageHelper.ScoreSubtitleCandidate(subtitle, request.SourceLanguage);

        if (TitlesMatch(subtitle.Title, request.SelectedStreamTitle))
        {
            score += 120;
        }

        if (subtitle.IsForced == request.IsForcedSubtitle)
        {
            score += 30;
        }
        else if (request.IsForcedSubtitle)
        {
            score -= 30;
        }

        var requestType = request.SourceSubtitleType ?? string.Empty;
        var subtitleType = DetermineSubtitleType(subtitle);
        if (!string.IsNullOrWhiteSpace(requestType) &&
            string.Equals(requestType, subtitleType, StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        return score;
    }

    private static bool TitlesMatch(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

private static string DetermineSubtitleType(EmbeddedSubtitle subtitle)
    {
        var title = (subtitle.Title ?? string.Empty).ToLowerInvariant();

        if (title.Contains("sdh") || title.Contains("hearing") || title.Contains("deaf"))
        {
            return "SDH";
        }

        if (title.Contains("forced") || title.Contains("force") || title.Contains("foreign"))
        {
            return "Forced";
        }

        if (title.Contains("full") || title.Contains("dialogue") || title.Contains("complete"))
        {
            return "Full";
        }

        if (title.Contains("sign") || title.Contains("song"))
        {
            return "Signs/Songs";
        }

        if (subtitle.IsForced)
        {
            return "Forced";
        }

        return "Unknown";
    }

    private void CleanupTemporarySubtitle(ResolvedSubtitlePath? subtitle)
    {
        if (subtitle == null || !subtitle.CleanupAfterRead)
        {
            return;
        }

        if (!System.IO.File.Exists(subtitle.Path))
        {
            return;
        }

        try
        {
            System.IO.File.Delete(subtitle.Path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temporary compare subtitle: {Path}", subtitle.Path);
        }
    }

    private static List<SubtitleItem> RemoveTranslatorInfoLines(List<SubtitleItem> subtitles)
    {
        return subtitles
            .Where(subtitle =>
            {
                var text = CombineSubtitleLines(subtitle);
                return !text.StartsWith(TranslatorInfoPrefix, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
    }

    private static List<TranslationCompareLineDto> BuildLineComparison(
        List<SubtitleItem> originalSubtitles,
        List<SubtitleItem> translatedSubtitles)
    {
        var lineCount = Math.Max(originalSubtitles.Count, translatedSubtitles.Count);
        var result = new List<TranslationCompareLineDto>(lineCount);

        for (int i = 0; i < lineCount; i++)
        {
            var original = i < originalSubtitles.Count ? originalSubtitles[i] : null;
            var translated = i < translatedSubtitles.Count ? translatedSubtitles[i] : null;

            var originalText = original != null ? CombineSubtitleLines(original) : string.Empty;
            var translatedText = translated != null ? CombineSubtitleLines(translated) : string.Empty;

            var startTime = original?.StartTime ?? translated?.StartTime;
            var endTime = original?.EndTime ?? translated?.EndTime;
            int? duration = null;
            if (startTime.HasValue && endTime.HasValue)
            {
                duration = Math.Max(0, endTime.Value - startTime.Value);
            }

            result.Add(new TranslationCompareLineDto
            {
                Position = original?.Position ?? translated?.Position ?? i + 1,
                Original = originalText,
                Translated = string.IsNullOrWhiteSpace(translatedText) ? null : translatedText,
                Success = !string.IsNullOrWhiteSpace(translatedText),
                DurationMs = duration,
                StartTimeMs = startTime,
                EndTimeMs = endTime
            });
        }

        return result;
    }

    private static string CombineSubtitleLines(SubtitleItem subtitle)
    {
        if (subtitle.Lines.Count > 0)
        {
            return string.Join(" ", subtitle.Lines);
        }

        if (subtitle.PlaintextLines.Count > 0)
        {
            return string.Join(" ", subtitle.PlaintextLines);
        }

        return string.Empty;
    }

    private sealed record ResolvedSubtitlePath(string Path, bool CleanupAfterRead);

    private sealed record SourceExtractionCandidate(string MediaPath, EmbeddedSubtitle Subtitle);
}
