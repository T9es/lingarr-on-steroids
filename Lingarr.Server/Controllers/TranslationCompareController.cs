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
    private readonly ISubtitleService _subtitleService;
    private readonly ILogger<TranslationCompareController> _logger;

    public TranslationCompareController(
        LingarrDbContext dbContext,
        ISubtitleService subtitleService,
        ILogger<TranslationCompareController> logger)
    {
        _dbContext = dbContext;
        _subtitleService = subtitleService;
        _logger = logger;
    }

    [HttpGet("{requestId:int}")]
    public async Task<ActionResult<CompletedTranslationCompareResponse>> GetCompletedTranslationCompare(
        int requestId,
        CancellationToken cancellationToken = default)
    {
        var request = await _dbContext.TranslationRequests
            .AsNoTracking()
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

        if (string.IsNullOrWhiteSpace(request.TranslatedSubtitle))
        {
            return NotFound(new
            {
                message =
                    $"Translation request {requestId} does not contain a translated subtitle path."
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

        if (!System.IO.File.Exists(request.TranslatedSubtitle))
        {
            return NotFound(new
            {
                message =
                    $"Translated subtitle file does not exist on disk: {request.TranslatedSubtitle}"
            });
        }

        try
        {
            var originalSubtitles = await _subtitleService.ReadSubtitles(request.SubtitleToTranslate);
            var translatedSubtitles = await _subtitleService.ReadSubtitles(request.TranslatedSubtitle);
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
                TranslatedSubtitlePath = request.TranslatedSubtitle,
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
