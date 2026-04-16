using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Models.FileSystem;
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
    private readonly ISubtitleService _subtitleService;
    private readonly ILogger<TranslationCompareController> _logger;

    public TranslationCompareController(
        LingarrDbContext dbContext,
        ISettingService settingService,
        ISubtitleService subtitleService,
        ILogger<TranslationCompareController> logger)
    {
        _dbContext = dbContext;
        _settingService = settingService;
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

        if (!System.IO.File.Exists(request.SubtitleToTranslate))
        {
            return NotFound(new
            {
                message =
                    $"Source subtitle file does not exist on disk: {request.SubtitleToTranslate}"
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

        try
        {
            var originalSubtitles = await _subtitleService.ReadSubtitles(request.SubtitleToTranslate);
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
                OriginalSubtitlePath = request.SubtitleToTranslate,
                TranslatedSubtitlePath = translatedSubtitlePath,
                OriginalLineCount = originalSubtitles.Count,
                TranslatedLineCount = filteredTranslatedSubtitles.Count,
                Lines = lines
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build compare payload for translation request {RequestId}", requestId);
            return StatusCode(500, new { message = "Failed to load subtitle compare data." });
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
}
