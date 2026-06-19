using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Translation;
using Lingarr.Server.Services.Subtitle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;

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
    private readonly ITranslationCheckpointService _checkpointService;
    private readonly ILogger<TranslationCompareController> _logger;
    private string? _tempTranslatedComparePath;

    public TranslationCompareController(
        LingarrDbContext dbContext,
        ISettingService settingService,
        ISubtitleExtractionService extractionService,
        ISourceSubtitleResolver sourceSubtitleResolver,
        ISubtitleService subtitleService,
        ITranslationCheckpointService checkpointService,
        ILogger<TranslationCompareController> logger)
    {
        _dbContext = dbContext;
        _settingService = settingService;
        _extractionService = extractionService;
        _sourceSubtitleResolver = sourceSubtitleResolver;
        _subtitleService = subtitleService;
        _checkpointService = checkpointService;
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

        if (request.Status != TranslationStatus.Completed &&
            request.Status != TranslationStatus.Failed)
        {
            return BadRequest(new
            {
                message =
                    $"Translation request {requestId} is not completed or failed. Current status: {request.Status}."
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

            var originalSubtitles = await _subtitleService.ReadSubtitles(originalSubtitle.Path);

            List<SubtitleItem> filteredTranslatedSubtitles;
            string? translatedSubtitlePath;
            bool isPartialFailure = false;
            List<int> missingPositions = [];

            if (request.Status == TranslationStatus.Failed)
            {
                var checkpoint = await _checkpointService.LoadByRequestIdAsync(
                    request.Id, cancellationToken);

                if (checkpoint == null || checkpoint.Translations.Count == 0)
                {
                    return NotFound(new
                    {
                        message =
                            $"No translation checkpoint found for failed request {requestId}. Cannot build comparison."
                    });
                }

                missingPositions = ParseMissingPositions(request.Id);
                isPartialFailure = missingPositions.Count > 0;

                filteredTranslatedSubtitles = BuildFailedComparisonSubtitles(
                    originalSubtitles,
                    checkpoint.Translations,
                    missingPositions);
                translatedSubtitlePath = request.SubtitleToTranslate ?? originalSubtitle.Path;
            }
            else
            {
                var translatedSubtitle =
                    await ResolveTranslatedSubtitlePathAsync(request, originalSubtitle.Path, cancellationToken);
                translatedSubtitlePath = translatedSubtitle?.Path;

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

                var translatedSubtitles = await _subtitleService.ReadSubtitles(translatedSubtitlePath);
                filteredTranslatedSubtitles = RemoveTranslatorInfoLines(translatedSubtitles);
            }

            var lines = BuildLineComparison(originalSubtitles, filteredTranslatedSubtitles);

            if (isPartialFailure)
            {
                var missingSet = new HashSet<int>(missingPositions);
                foreach (var line in lines)
                {
                    if (missingSet.Contains(line.Position))
                    {
                        line.IsMissing = true;
                        line.CanEdit = true;
                        line.Translated = null;
                        line.Success = false;
                    }
                    else
                    {
                        line.CanEdit = true;
                    }
                }
            }
            else if (request.Status == TranslationStatus.Completed)
            {
                foreach (var line in lines)
                {
                    line.CanEdit = true;
                }
            }

            var response = new CompletedTranslationCompareResponse
            {
                TranslationRequestId = request.Id,
                Title = request.Title,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                MediaType = request.MediaType.ToString(),
                CompletedAt = request.CompletedAt,
                OriginalSubtitlePath = originalSubtitle.Path,
                TranslatedSubtitlePath = translatedSubtitlePath ?? originalSubtitle.Path,
                OriginalLineCount = originalSubtitles.Count,
                TranslatedLineCount = filteredTranslatedSubtitles.Count,
                Lines = lines,
                IsPartialFailure = isPartialFailure,
                MissingPositions = missingPositions,
                CanAccept = request.Status == TranslationStatus.Failed
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
                    _logger.LogWarning(
                        ex,
                        "Failed to delete temporary translated compare subtitle: {Path}",
                        _tempTranslatedComparePath);
                }
            }
        }
    }

    [HttpPost("{requestId:int}/accept")]
    public async Task<ActionResult<CompletedTranslationCompareResponse>> AcceptTranslation(
        int requestId,
        [FromBody] TranslationCompareEditRequest? editRequest,
        CancellationToken cancellationToken = default)
    {
        var request = await _dbContext.TranslationRequests
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

        if (request == null)
        {
            return NotFound(new { message = $"Translation request {requestId} was not found." });
        }

        if (request.Status != TranslationStatus.Failed)
        {
            return BadRequest(new
            {
                message =
                    $"Translation request {requestId} is not in Failed status. Current status: {request.Status}."
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
            if (originalSubtitle == null || !System.IO.File.Exists(originalSubtitle.Path))
            {
                return NotFound(new
                {
                    message =
                        $"Source subtitle file does not exist on disk: {request.SubtitleToTranslate}"
                });
            }

            var checkpoint = await _checkpointService.LoadByRequestIdAsync(
                request.Id, cancellationToken);

            if (checkpoint == null || checkpoint.Translations.Count == 0)
            {
                return NotFound(new
                {
                    message =
                        $"No translation checkpoint found for failed request {requestId}."
                });
            }

            var missingPositions = ParseMissingPositions(request.Id);
            var originalSubtitles = await _subtitleService.ReadSubtitles(originalSubtitle.Path);

            var edits = editRequest?.Edits
                ?.ToDictionary(e => e.Position, e => e.TranslatedText)
                ?? new Dictionary<int, string>();

            var outputSubtitles = new List<SubtitleItem>();
            foreach (var original in originalSubtitles)
            {
                var item = new SubtitleItem
                {
                    Position = original.Position,
                    StartTime = original.StartTime,
                    EndTime = original.EndTime,
                    Lines = [.. original.Lines],
                    PlaintextLines = [.. original.PlaintextLines],
                    TranslatedLines = [.. original.TranslatedLines],
                    SsaDialogue = original.SsaDialogue,
                    SsaFormat = original.SsaFormat
                };

                if (edits.TryGetValue(original.Position, out var editText))
                {
                    item.TranslatedLines = [editText];
                }
                else if (checkpoint.Translations.TryGetValue(original.Position, out var translated))
                {
                    item.TranslatedLines = [translated];
                }
                else if (missingPositions.Contains(original.Position))
                {
                    item.TranslatedLines = [.. original.Lines];
                }

                outputSubtitles.Add(item);
            }

            var settings = await _settingService.GetSettings([
                SettingKeys.Translation.UseSubtitleTagging,
                SettingKeys.Translation.RemoveLanguageTag,
                SettingKeys.Translation.SubtitleTag,
                SettingKeys.Translation.SubtitleTagShort,
                SettingKeys.Translation.StripSubtitleFormatting
            ]);

            var useSubtitleTagging =
                settings.TryGetValue(SettingKeys.Translation.UseSubtitleTagging, out var useTaggingValue) &&
                string.Equals(useTaggingValue, "true", StringComparison.OrdinalIgnoreCase);
            var removeLanguageTag =
                settings.TryGetValue(SettingKeys.Translation.RemoveLanguageTag, out var removeLanguageTagValue) &&
                string.Equals(removeLanguageTagValue, "true", StringComparison.OrdinalIgnoreCase);
            var subtitleTag = useSubtitleTagging
                ? settings.GetValueOrDefault(SettingKeys.Translation.SubtitleTag) ?? string.Empty
                : string.Empty;
            var subtitleTagShort = useSubtitleTagging
                ? settings.GetValueOrDefault(SettingKeys.Translation.SubtitleTagShort) ?? string.Empty
                : string.Empty;

            var targetLanguage = removeLanguageTag ? string.Empty : request.TargetLanguage;
            var outputPath = _subtitleService.CreateFilePath(
                request.SubtitleToTranslate,
                targetLanguage,
                subtitleTag);

            var stripFormatting =
                settings.TryGetValue(SettingKeys.Translation.StripSubtitleFormatting, out var stripVal) &&
                string.Equals(stripVal, "true", StringComparison.OrdinalIgnoreCase);

            await _subtitleService.WriteSubtitles(outputPath, outputSubtitles, stripFormatting);

            request.Status = TranslationStatus.Completed;
            request.CompletedAt = DateTime.UtcNow;
            request.TranslatedSubtitle = outputPath;
            request.IsActive = null;
            request.NextRetryAt = null;
            request.PausedAt = null;
            request.PauseReason = null;
            request.PausedProvider = null;

            _dbContext.TranslationRequestLogs.Add(new TranslationRequestLog
            {
                TranslationRequestId = request.Id,
                Level = "Information",
                Message = $"Translation accepted with {edits.Count} manual edit(s). Untranslated position(s) preserved as source text."
            });

            await _dbContext.SaveChangesAsync(cancellationToken);

            await _checkpointService.DeleteAsync(request.Id, cancellationToken);

            _logger.LogInformation(
                "Accepted failed translation request {RequestId} with {EditCount} edits",
                requestId,
                edits.Count);

            var response = BuildCompareResponse(
                request,
                originalSubtitle.Path,
                outputPath,
                originalSubtitles,
                outputSubtitles,
                missingPositions,
                isPartialFailure: false);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to accept translation for request {RequestId}", requestId);
            return StatusCode(500, new { message = "Failed to accept translation." });
        }
        finally
        {
            CleanupTemporarySubtitle(originalSubtitle);
        }
    }

    [HttpPost("{requestId:int}/save")]
    public async Task<ActionResult<CompletedTranslationCompareResponse>> SaveTranslation(
        int requestId,
        [FromBody] TranslationCompareEditRequest editRequest,
        CancellationToken cancellationToken = default)
    {
        if (editRequest?.Edits == null || editRequest.Edits.Count == 0)
        {
            return BadRequest(new { message = "At least one edit is required." });
        }

        var request = await _dbContext.TranslationRequests
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

        if (request == null)
        {
            return NotFound(new { message = $"Translation request {requestId} was not found." });
        }

        if (request.Status != TranslationStatus.Completed &&
            request.Status != TranslationStatus.Failed)
        {
            return BadRequest(new
            {
                message =
                    $"Translation request {requestId} is not completed or failed. Current status: {request.Status}."
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
            if (originalSubtitle == null || !System.IO.File.Exists(originalSubtitle.Path))
            {
                return NotFound(new
                {
                    message =
                        $"Source subtitle file does not exist on disk: {request.SubtitleToTranslate}"
                });
            }

            var originalSubtitles = await _subtitleService.ReadSubtitles(originalSubtitle.Path);
            var edits = editRequest.Edits.ToDictionary(e => e.Position, e => e.TranslatedText);

            if (request.Status == TranslationStatus.Failed)
            {
                var checkpoint = await _checkpointService.LoadByRequestIdAsync(
                    request.Id, cancellationToken);

                if (checkpoint == null)
                {
                    return NotFound(new
                    {
                        message =
                            $"No translation checkpoint found for failed request {requestId}."
                    });
                }

                foreach (var edit in edits)
                {
                    checkpoint.Translations[edit.Key] = edit.Value;
                }

                checkpoint.UpdatedAtUtc = DateTime.UtcNow;
                await _checkpointService.SaveCheckpointAsync(checkpoint, cancellationToken);

                var missingPositions = ParseMissingPositions(request.Id);
                var filteredSubtitles = BuildFailedComparisonSubtitles(
                    originalSubtitles,
                    checkpoint.Translations,
                    missingPositions);

                var response = BuildCompareResponse(
                    request,
                    originalSubtitle.Path,
                    request.SubtitleToTranslate ?? originalSubtitle.Path,
                    originalSubtitles,
                    filteredSubtitles,
                    missingPositions,
                    isPartialFailure: missingPositions.Count > 0);

                return Ok(response);
            }

            var translatedSubtitle =
                await ResolveTranslatedSubtitlePathAsync(request, originalSubtitle.Path, cancellationToken);
            var translatedSubtitlePath = translatedSubtitle?.Path;

            if (string.IsNullOrWhiteSpace(translatedSubtitlePath) ||
                !System.IO.File.Exists(translatedSubtitlePath))
            {
                return NotFound(new
                {
                    message =
                        $"Translated subtitle file not found for request {requestId}."
                });
            }

            var translatedSubtitles = await _subtitleService.ReadSubtitles(translatedSubtitlePath);
            var filteredTranslatedSubtitles = RemoveTranslatorInfoLines(translatedSubtitles);

            // Apply edits to both the full list (for disk write) and filtered list (for response)
            foreach (var subtitle in translatedSubtitles)
            {
                if (edits.TryGetValue(subtitle.Position, out var editText))
                {
                    subtitle.TranslatedLines = [editText];
                }
            }
            foreach (var subtitle in filteredTranslatedSubtitles)
            {
                if (edits.TryGetValue(subtitle.Position, out var editText))
                {
                    subtitle.TranslatedLines = [editText];
                }
            }

            var saveSettings = await _settingService.GetSettings([
                SettingKeys.Translation.StripSubtitleFormatting
            ]);

            var stripFormatting =
                saveSettings.TryGetValue(SettingKeys.Translation.StripSubtitleFormatting, out var stripVal) &&
                string.Equals(stripVal, "true", StringComparison.OrdinalIgnoreCase);

            // Write full subtitles (with translator info preserved) to disk
            await _subtitleService.WriteSubtitles(translatedSubtitlePath, translatedSubtitles, stripFormatting);

            _logger.LogInformation(
                "Saved edits for translation request {RequestId} with {EditCount} edits",
                requestId,
                edits.Count);

            // Use filtered subtitles (without translator info) for the compare response
            var saveResponse = BuildCompareResponse(
                request,
                originalSubtitle.Path,
                translatedSubtitlePath,
                originalSubtitles,
                filteredTranslatedSubtitles,
                [],
                isPartialFailure: false);

            return Ok(saveResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save translation for request {RequestId}", requestId);
            return StatusCode(500, new { message = "Failed to save translation." });
        }
        finally
        {
            CleanupTemporarySubtitle(originalSubtitle);
        }
    }

    /// <summary>
    /// Parses missing positions from the latest Error-level log entry for a failed translation request.
    /// </summary>
    private List<int> ParseMissingPositions(int requestId)
    {
        var latestErrorLog = _dbContext.TranslationRequestLogs
            .Where(l => l.TranslationRequestId == requestId && l.Level == "Error")
            .OrderByDescending(l => l.Id)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(latestErrorLog?.Details))
        {
            return [];
        }

        var positions = new HashSet<int>();

        var positionRangeMatch = Regex.Match(
            latestErrorLog.Details,
            @"missing at positions:\s*([\d,\s]+)");
        if (positionRangeMatch.Success)
        {
            var positionList = positionRangeMatch.Groups[1].Value;
            foreach (var part in positionList.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part.Trim(), out var pos))
                {
                    positions.Add(pos);
                }
            }
        }

        var posMatches = Regex.Matches(latestErrorLog.Details, @"pos (\d+):");
        foreach (Match m in posMatches)
        {
            if (int.TryParse(m.Groups[1].Value, out var pos))
            {
                positions.Add(pos);
            }
        }

        return positions.Order().ToList();
    }

    /// <summary>
    /// Builds subtitle items for a failed request by filling translated positions from the checkpoint
    /// and missing positions from the source.
    /// </summary>
    private static List<SubtitleItem> BuildFailedComparisonSubtitles(
        List<SubtitleItem> originalSubtitles,
        Dictionary<int, string> checkpointTranslations,
        List<int> missingPositions)
    {
        var missingSet = new HashSet<int>(missingPositions);
        return originalSubtitles.Select(original =>
        {
            var item = new SubtitleItem
            {
                Position = original.Position,
                StartTime = original.StartTime,
                EndTime = original.EndTime,
                Lines = [.. original.Lines],
                PlaintextLines = [.. original.PlaintextLines],
                TranslatedLines = [.. original.TranslatedLines],
                SsaDialogue = original.SsaDialogue,
                SsaFormat = original.SsaFormat
            };

            if (checkpointTranslations.TryGetValue(original.Position, out var translated))
            {
                item.TranslatedLines = [translated];
            }
            else if (missingSet.Contains(original.Position))
            {
                item.TranslatedLines = [string.Empty];
            }

            return item;
        }).ToList();
    }

    /// <summary>
    /// Builds a compare response from source, translated subtitles, and metadata.
    /// </summary>
    private CompletedTranslationCompareResponse BuildCompareResponse(
        TranslationRequest request,
        string originalPath,
        string translatedPath,
        List<SubtitleItem> originalSubtitles,
        List<SubtitleItem> translatedSubtitles,
        List<int> missingPositions,
        bool isPartialFailure)
    {
        var lines = BuildLineComparison(originalSubtitles, translatedSubtitles);

        if (missingPositions.Count > 0)
        {
            var missingSet = new HashSet<int>(missingPositions);
            foreach (var line in lines)
            {
                if (missingSet.Contains(line.Position))
                {
                    line.IsMissing = true;
                    line.CanEdit = true;
                    line.Translated = null;
                    line.Success = false;
                }
                else
                {
                    line.CanEdit = true;
                }
            }
        }
        else
        {
            foreach (var line in lines)
            {
                line.CanEdit = true;
            }
        }

        return new CompletedTranslationCompareResponse
        {
            TranslationRequestId = request.Id,
            Title = request.Title,
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            MediaType = request.MediaType.ToString(),
            CompletedAt = request.CompletedAt,
            OriginalSubtitlePath = originalPath,
            TranslatedSubtitlePath = translatedPath,
            OriginalLineCount = originalSubtitles.Count,
            TranslatedLineCount = translatedSubtitles.Count,
            Lines = lines,
            IsPartialFailure = isPartialFailure,
            MissingPositions = missingPositions,
            CanAccept = request.Status == TranslationStatus.Failed
        };
    }

    private async Task<ResolvedTranslatedSubtitlePath?> ResolveTranslatedSubtitlePathAsync(
        TranslationRequest request,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.TranslatedSubtitle) &&
            !request.TranslatedSubtitle.StartsWith("mkv-embedded:", StringComparison.OrdinalIgnoreCase) &&
            !IsSamePath(request.TranslatedSubtitle, sourcePath) &&
            System.IO.File.Exists(request.TranslatedSubtitle))
        {
            return new ResolvedTranslatedSubtitlePath(
                request.TranslatedSubtitle,
                request.TranslatedSubtitle);
        }
        // Check for mkv-embedded marker — the subtitle was embedded into the MKV
        // container rather than written as a standalone file
        if (request.TranslatedSubtitle?.StartsWith("mkv-embedded:", StringComparison.OrdinalIgnoreCase) == true)
        {
            var extractedPath = await ExtractTranslatedSubtitleFromMkvAsync(
                request.TranslatedSubtitle, request, cancellationToken);
            return string.IsNullOrWhiteSpace(extractedPath)
                ? null
                : new ResolvedTranslatedSubtitlePath(
                    extractedPath,
                    request.TranslatedSubtitle);
        }

        if (string.IsNullOrWhiteSpace(request.SubtitleToTranslate))
        {
            return null;
        }

        foreach (var generatedPath in GetGeneratedSubtitlePaths(request))
        {
            var generated = await TryResolveTranslatedCandidateAsync(
                generatedPath,
                sourcePath,
                request,
                cancellationToken);
            if (generated != null)
            {
                await PersistResolvedTranslatedPathAsync(request, generated.PersistentPath, cancellationToken);
                return generated;
            }
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

        if (!string.IsNullOrWhiteSpace(request.TranslatedSubtitle) &&
            !request.TranslatedSubtitle.StartsWith("mkv-embedded:", StringComparison.OrdinalIgnoreCase) &&
            !IsSamePath(request.TranslatedSubtitle, sourcePath))
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
                    if (!IsSamePath(candidatePath, sourcePath))
                    {
                        candidatePaths.Add(candidatePath);
                    }
                }
            }
        }

        var resolvedPath = candidatePaths.FirstOrDefault(System.IO.File.Exists);
        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            return null;
        }

        await PersistResolvedTranslatedPathAsync(request, resolvedPath, cancellationToken);

        return new ResolvedTranslatedSubtitlePath(resolvedPath, resolvedPath);
    }

    private async Task<ResolvedTranslatedSubtitlePath?> TryResolveTranslatedCandidateAsync(
        string? candidate,
        string sourcePath,
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        if (candidate.StartsWith("mkv-embedded:", StringComparison.OrdinalIgnoreCase))
        {
            var extractedPath = await ExtractTranslatedSubtitleFromMkvAsync(candidate, request, cancellationToken);
            return string.IsNullOrWhiteSpace(extractedPath)
                ? null
                : new ResolvedTranslatedSubtitlePath(extractedPath, candidate);
        }

        if (IsSamePath(candidate, sourcePath) || !System.IO.File.Exists(candidate))
        {
            return null;
        }

        return new ResolvedTranslatedSubtitlePath(candidate, candidate);
    }

    private async Task PersistResolvedTranslatedPathAsync(
        TranslationRequest request,
        string? persistentPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(persistentPath) ||
            string.Equals(request.TranslatedSubtitle, persistentPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        request.TranslatedSubtitle = persistentPath;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static List<string> GetGeneratedSubtitlePaths(TranslationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GeneratedSubtitlePaths))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(request.GeneratedSubtitlePaths) ?? [];
        }
        catch
        {
            return request.GeneratedSubtitlePaths.Split(
                    '|',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }
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

        var lingarrSubtitles = embeddedSubtitles
            .Where(s => s.IsTextBased &&
                        s.Title?.Contains("Lingarr", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        var selected = lingarrSubtitles.FirstOrDefault(s =>
            SubtitleLanguageHelper.LanguageMatches(s.Language, targetLanguage));

        selected ??= lingarrSubtitles.FirstOrDefault(HasMissingLanguage);

        if (selected == null)
        {
            _logger.LogWarning(
                "No matching Lingarr translated subtitle stream found in MKV: {MkvPath} (target language: {TargetLanguage})",
                mkvPath,
                request.TargetLanguage);
            return null;
        }

        // Extract to a temporary directory for compare using a short filename
        // to avoid "File name too long" errors when MKV filenames approach the 255-char limit
        var tempDir = Path.Combine(Path.GetTempPath(), "lingarr_translated_compare");
        var extension = selected.CodecName?.ToLowerInvariant() switch
        {
            "ass" or "ssa" => ".ass",
            "webvtt" or "vtt" => ".vtt",
            _ => ".srt"
        };
        var outputPath = Path.Combine(tempDir, $"lingarr_compare_{request.Id}_{selected.StreamIndex}{extension}");
        var extractedPath = await _extractionService.ExtractSubtitleToFile(
            mkvPath,
            selected.StreamIndex,
            outputPath,
            selected.CodecName ?? string.Empty);

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

    private static bool HasMissingLanguage(EmbeddedSubtitle subtitle)
    {
        if (string.IsNullOrWhiteSpace(subtitle.Language))
        {
            return true;
        }

        return subtitle.Language.Equals("und", StringComparison.OrdinalIgnoreCase) ||
               subtitle.Language.Equals("unknown", StringComparison.OrdinalIgnoreCase);
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
        translatedSubtitles = NormalizeTranslatedPositionsForComparison(originalSubtitles, translatedSubtitles);

        if (CanCompareByPosition(originalSubtitles, translatedSubtitles))
        {
            return BuildPositionComparison(originalSubtitles, translatedSubtitles);
        }

        return BuildIndexComparison(originalSubtitles, translatedSubtitles);
    }

    private static List<TranslationCompareLineDto> BuildIndexComparison(
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

    private static List<TranslationCompareLineDto> BuildPositionComparison(
        List<SubtitleItem> originalSubtitles,
        List<SubtitleItem> translatedSubtitles)
    {
        var originalByPosition = originalSubtitles.ToDictionary(subtitle => subtitle.Position);
        var translatedByPosition = translatedSubtitles.ToDictionary(subtitle => subtitle.Position);
        var positions = originalByPosition.Keys
            .Union(translatedByPosition.Keys)
            .Order()
            .ToList();
        var result = new List<TranslationCompareLineDto>(positions.Count);

        foreach (var position in positions)
        {
            originalByPosition.TryGetValue(position, out var original);
            translatedByPosition.TryGetValue(position, out var translated);

            result.Add(BuildCompareLine(position, original, translated));
        }

        return result;
    }

    private static TranslationCompareLineDto BuildCompareLine(
        int position,
        SubtitleItem? original,
        SubtitleItem? translated)
    {
        var originalText = original != null ? CombineSubtitleLines(original) : string.Empty;
        var translatedText = translated != null ? CombineSubtitleLines(translated) : string.Empty;

        var startTime = original?.StartTime ?? translated?.StartTime;
        var endTime = original?.EndTime ?? translated?.EndTime;
        int? duration = null;
        if (startTime.HasValue && endTime.HasValue)
        {
            duration = Math.Max(0, endTime.Value - startTime.Value);
        }

        return new TranslationCompareLineDto
        {
            Position = position,
            Original = originalText,
            Translated = string.IsNullOrWhiteSpace(translatedText) ? null : translatedText,
            Success = !string.IsNullOrWhiteSpace(translatedText),
            DurationMs = duration,
            StartTimeMs = startTime,
            EndTimeMs = endTime
        };
    }

    private static bool CanCompareByPosition(
        IReadOnlyCollection<SubtitleItem> originalSubtitles,
        IReadOnlyCollection<SubtitleItem> translatedSubtitles)
    {
        return HasUniquePositions(originalSubtitles) && HasUniquePositions(translatedSubtitles);
    }

    private static bool HasUniquePositions(IReadOnlyCollection<SubtitleItem> subtitles)
    {
        return subtitles.Select(subtitle => subtitle.Position).Distinct().Count() == subtitles.Count;
    }

    private static List<SubtitleItem> NormalizeTranslatedPositionsForComparison(
        IReadOnlyList<SubtitleItem> originalSubtitles,
        IReadOnlyList<SubtitleItem> translatedSubtitles)
    {
        if (originalSubtitles.Count == 0 ||
            originalSubtitles.Count != translatedSubtitles.Count ||
            PositionsMatchByIndex(originalSubtitles, translatedSubtitles) ||
            !HasConstantPositionOffset(originalSubtitles, translatedSubtitles))
        {
            return translatedSubtitles.ToList();
        }

        return translatedSubtitles
            .Select((subtitle, index) => CloneWithPosition(subtitle, originalSubtitles[index].Position))
            .ToList();
    }

    private static bool PositionsMatchByIndex(
        IReadOnlyList<SubtitleItem> originalSubtitles,
        IReadOnlyList<SubtitleItem> translatedSubtitles)
    {
        return originalSubtitles
            .Select((subtitle, index) => subtitle.Position == translatedSubtitles[index].Position)
            .All(matches => matches);
    }

    private static bool HasConstantPositionOffset(
        IReadOnlyList<SubtitleItem> originalSubtitles,
        IReadOnlyList<SubtitleItem> translatedSubtitles)
    {
        var offset = translatedSubtitles[0].Position - originalSubtitles[0].Position;
        return originalSubtitles
            .Select((subtitle, index) => translatedSubtitles[index].Position - subtitle.Position == offset)
            .All(matches => matches);
    }

    private static SubtitleItem CloneWithPosition(SubtitleItem subtitle, int position)
    {
        return new SubtitleItem
        {
            Position = position,
            StartTime = subtitle.StartTime,
            EndTime = subtitle.EndTime,
            Lines = [.. subtitle.Lines],
            PlaintextLines = [.. subtitle.PlaintextLines],
            TranslatedLines = [.. subtitle.TranslatedLines],
            SsaDialogue = subtitle.SsaDialogue,
            SsaFormat = subtitle.SsaFormat
        };
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

    private sealed record ResolvedTranslatedSubtitlePath(string Path, string? PersistentPath);

    private sealed record SourceExtractionCandidate(string MediaPath, EmbeddedSubtitle Subtitle);

    private static bool IsSamePath(string path, string? otherPath)
    {
        return !string.IsNullOrWhiteSpace(otherPath) &&
               string.Equals(
                   Path.GetFullPath(path),
                   Path.GetFullPath(otherPath),
                   StringComparison.OrdinalIgnoreCase);
    }
}
