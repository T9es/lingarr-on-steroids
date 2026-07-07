using System.Text.RegularExpressions;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.Subtitle;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.Subtitle;

public class SubtitleOcrService : ISubtitleOcrService
{
    private static readonly HashSet<string> SupportedCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "hdmv_pgs_subtitle",
        "pgssub"
    };

    private static readonly HashSet<string> DetectedButDisabledCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "dvd_subtitle",
        "dvb_subtitle",
        "xsub"
    };

    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settingService;
    private readonly ISubtitleService _subtitleService;
    private readonly IEmbeddedSubtitleCacheService _embeddedSubtitleCacheService;
    private readonly ISubtitleOcrEngine _ocrEngine;
    private readonly IMediaStateService _mediaStateService;
    private readonly ILogger<SubtitleOcrService> _logger;

    public SubtitleOcrService(
        LingarrDbContext dbContext,
        ISettingService settingService,
        ISubtitleService subtitleService,
        IEmbeddedSubtitleCacheService embeddedSubtitleCacheService,
        ISubtitleOcrEngine ocrEngine,
        IMediaStateService mediaStateService,
        ILogger<SubtitleOcrService> logger)
    {
        _dbContext = dbContext;
        _settingService = settingService;
        _subtitleService = subtitleService;
        _embeddedSubtitleCacheService = embeddedSubtitleCacheService;
        _ocrEngine = ocrEngine;
        _mediaStateService = mediaStateService;
        _logger = logger;
    }

    public bool IsSupportedCodec(string? codecName)
    {
        return !string.IsNullOrWhiteSpace(codecName) && SupportedCodecs.Contains(codecName);
    }

    public async Task<SubtitleOcrResult> QueueOcrAsync(
        int mediaId,
        MediaType mediaType,
        int streamIndex,
        bool manual,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadContextAsync(mediaId, mediaType, streamIndex, cancellationToken);
        if (context == null)
        {
            return Fail(SubtitleOcrStatus.Failed, "Embedded subtitle stream was not found.");
        }

        var validationError = await ValidateCanQueueAsync(context.Subtitle, cancellationToken);
        if (validationError != null)
        {
            context.Subtitle.OcrStatus = SubtitleOcrStatus.Failed;
            context.Subtitle.OcrError = validationError;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Fail(SubtitleOcrStatus.Failed, validationError);
        }

        var reEvaluatedResult = await TryReEvaluateBlockedLowQualityOcrAsync(
            context,
            manual,
            cancellationToken);
        if (reEvaluatedResult != null)
        {
            return reEvaluatedResult;
        }

        context.Subtitle.OcrStatus = SubtitleOcrStatus.Queued;
        context.Subtitle.OcrError = null;
        context.Subtitle.OcrAttemptedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return FromSubtitle(context.Subtitle, success: true);
    }

    public async Task<SubtitleOcrResult> RunOcrAsync(
        int mediaId,
        MediaType mediaType,
        int streamIndex,
        bool manual,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadContextAsync(mediaId, mediaType, streamIndex, cancellationToken);
        if (context == null)
        {
            return Fail(SubtitleOcrStatus.Failed, "Embedded subtitle stream was not found.");
        }

        var validationError = await ValidateCanQueueAsync(context.Subtitle, cancellationToken);
        if (validationError != null)
        {
            var failedSubtitle = await MarkFailedAsync(context, validationError, cancellationToken);
            return FromSubtitle(failedSubtitle, success: false);
        }

        context.Subtitle.OcrStatus = SubtitleOcrStatus.Processing;
        context.Subtitle.OcrError = null;
        context.Subtitle.OcrAttemptedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var outputPath = _embeddedSubtitleCacheService.GetOcrCachePath(
            mediaId,
            mediaType,
            streamIndex,
            context.Subtitle.Language);
        var tesseractLanguage = await ResolveOcrLanguageAsync(context.Subtitle, cancellationToken);

        var engineResult = await _ocrEngine.ConvertAsync(
            context.MediaPath,
            streamIndex,
            outputPath,
            tesseractLanguage,
            cancellationToken);
        if (!engineResult.Success || string.IsNullOrWhiteSpace(engineResult.OutputPath))
        {
            var failedSubtitle = await MarkFailedAsync(
                context,
                engineResult.Error ?? "OCR engine did not produce output.",
                cancellationToken);
            return FromSubtitle(failedSubtitle, success: false);
        }

        try
        {
            NormalizeOcrOutputFile(engineResult.OutputPath);
            var subtitles = await _subtitleService.ReadSubtitles(engineResult.OutputPath);
            var quality = SubtitleOcrQualityAnalyzer.Analyze(
                subtitles,
                await GetMinimumQualityScoreAsync(cancellationToken),
                manual && SubtitleLanguageHelper.IsSupplementalSubtitleType(
                    SubtitleLanguageHelper.DetermineSubtitleType(context.Subtitle)));

            var completedSubtitle = await SaveCurrentSubtitleStateAsync(
                context,
                streamIndex,
                subtitle =>
                {
                    subtitle.OcrExtractedPath = engineResult.OutputPath;
                    subtitle.OcrCueCount = quality.CueCount;
                    subtitle.OcrQualityScore = quality.QualityScore;
                    subtitle.OcrIssueSummary = quality.IssueSummary;
                    subtitle.OcrCompletedAt = DateTime.UtcNow;
                    subtitle.OcrError = quality.Accepted ? null : quality.IssueSummary;
                    subtitle.OcrStatus = quality.Accepted
                        ? SubtitleOcrStatus.Succeeded
                        : SubtitleOcrStatus.BlockedLowQuality;
                },
                cancellationToken);
            await _mediaStateService.UpdateStateAsync(context.Media, mediaType);

            _logger.LogInformation(
                "OCR completed for {MediaType} {MediaId} stream {StreamIndex}: status={Status}, cues={CueCount}, quality={QualityScore}",
                mediaType,
                mediaId,
                streamIndex,
                completedSubtitle.OcrStatus,
                completedSubtitle.OcrCueCount,
                completedSubtitle.OcrQualityScore);

            if (!quality.Accepted)
            {
                var corruptSamples = subtitles
                    .Where(s =>
                    {
                        var text = string.Join(' ', s.Lines).Trim();
                        return !string.IsNullOrWhiteSpace(text) &&
                               SubtitleSemanticClassifier.IsLikelyCorruptText(text);
                    })
                    .Take(10)
                    .Select(s =>
                    {
                        var raw = $"[{s.Position}] {string.Join(' ', s.Lines).Trim()}";
                        return raw.Length > 200 ? raw[..197] + "..." : raw;
                    })
                    .ToList();

                if (corruptSamples.Count > 0)
                {
                    _logger.LogWarning(
                        "OCR blocked for {MediaType} {MediaId} stream {StreamIndex}: quality={QualityScore}, issues={Issues}. Corrupt sample lines: {Samples}",
                        mediaType,
                        mediaId,
                        streamIndex,
                        quality.QualityScore,
                        quality.IssueSummary,
                        string.Join(" | ", corruptSamples));
                }
                else
                {
                    _logger.LogWarning(
                        "OCR blocked for {MediaType} {MediaId} stream {StreamIndex}: quality={QualityScore}, issues={Issues}",
                        mediaType,
                        mediaId,
                        streamIndex,
                        quality.QualityScore,
                        quality.IssueSummary);
                }
            }

            return FromSubtitle(completedSubtitle, success: quality.Accepted);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var failedSubtitle = await MarkFailedAsync(
                context,
                $"OCR output could not be parsed: {ex.Message}",
                cancellationToken);
            return FromSubtitle(failedSubtitle, success: false);
        }
    }

    public async Task<SubtitleOcrResult> ApproveOcrAsync(
        int mediaId,
        MediaType mediaType,
        int streamIndex,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadContextAsync(mediaId, mediaType, streamIndex, cancellationToken);
        if (context == null)
        {
            return Fail(SubtitleOcrStatus.Failed, "Embedded subtitle stream was not found.");
        }

        if (string.IsNullOrWhiteSpace(context.Subtitle.OcrExtractedPath) ||
            !File.Exists(context.Subtitle.OcrExtractedPath))
        {
            return Fail(context.Subtitle.OcrStatus, "No OCR output is available to approve.");
        }

        context.Subtitle.OcrStatus = SubtitleOcrStatus.Approved;
        context.Subtitle.OcrApprovedAt = DateTime.UtcNow;
        context.Subtitle.OcrError = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _mediaStateService.UpdateStateAsync(context.Media, mediaType);

        return FromSubtitle(context.Subtitle, success: true);
    }

    public async Task<SubtitleOcrPreviewResponse> GetPreviewAsync(
        int mediaId,
        MediaType mediaType,
        int streamIndex,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadContextAsync(mediaId, mediaType, streamIndex, cancellationToken);
        if (context == null)
        {
            return new SubtitleOcrPreviewResponse
            {
                Success = false,
                Status = SubtitleOcrStatus.Failed,
                Error = "Embedded subtitle stream was not found."
            };
        }

        if (string.IsNullOrWhiteSpace(context.Subtitle.OcrExtractedPath) ||
            !File.Exists(context.Subtitle.OcrExtractedPath))
        {
            return new SubtitleOcrPreviewResponse
            {
                Success = false,
                Status = context.Subtitle.OcrStatus,
                CueCount = context.Subtitle.OcrCueCount,
                QualityScore = context.Subtitle.OcrQualityScore,
                IssueSummary = context.Subtitle.OcrIssueSummary,
                Error = "No OCR output is available yet."
            };
        }

        try
        {
            var subtitles = await _subtitleService.ReadSubtitles(context.Subtitle.OcrExtractedPath);
            return new SubtitleOcrPreviewResponse
            {
                Success = true,
                Status = context.Subtitle.OcrStatus,
                CueCount = context.Subtitle.OcrCueCount ?? subtitles.Count,
                QualityScore = context.Subtitle.OcrQualityScore,
                IssueSummary = context.Subtitle.OcrIssueSummary,
                Lines = subtitles
                    .Take(25)
                    .Select(subtitle => new SubtitleOcrPreviewLine
                    {
                        Position = subtitle.Position,
                        StartTime = subtitle.StartTime,
                        EndTime = subtitle.EndTime,
                        Text = string.Join(
                            Environment.NewLine,
                            subtitle.PlaintextLines.Count > 0 ? subtitle.PlaintextLines : subtitle.Lines)
                    })
                    .ToList()
            };
        }
        catch (Exception ex)
        {
            return new SubtitleOcrPreviewResponse
            {
                Success = false,
                Status = context.Subtitle.OcrStatus,
                CueCount = context.Subtitle.OcrCueCount,
                QualityScore = context.Subtitle.OcrQualityScore,
                IssueSummary = context.Subtitle.OcrIssueSummary,
                Error = ex.Message
            };
        }
    }

    private async Task<string?> ValidateCanQueueAsync(
        EmbeddedSubtitle subtitle,
        CancellationToken cancellationToken)
    {
        if (subtitle.IsTextBased)
        {
            return "Text-based subtitles do not need OCR.";
        }

        if (!await IsOcrEnabledAsync(cancellationToken))
        {
            return "Subtitle OCR is disabled in settings.";
        }

        if (!IsSupportedCodec(subtitle.CodecName))
        {
            if (DetectedButDisabledCodecs.Contains(subtitle.CodecName))
            {
                return $"OCR for {subtitle.CodecName} is detected but not enabled yet. Only Blu-ray PGS is supported.";
            }

            return $"OCR does not support subtitle codec {subtitle.CodecName}.";
        }

        return null;
    }

    private async Task<SubtitleOcrResult?> TryReEvaluateBlockedLowQualityOcrAsync(
        OcrMediaContext context,
        bool manual,
        CancellationToken cancellationToken)
    {
        var subtitle = context.Subtitle;
        if (subtitle.OcrStatus != SubtitleOcrStatus.BlockedLowQuality ||
            string.IsNullOrWhiteSpace(subtitle.OcrExtractedPath) ||
            !subtitle.OcrQualityScore.HasValue ||
            !File.Exists(subtitle.OcrExtractedPath))
        {
            return null;
        }

        var minimumQualityScore = await GetMinimumQualityScoreAsync(cancellationToken);
        if (subtitle.OcrQualityScore.Value < minimumQualityScore)
        {
            if (manual)
            {
                return null;
            }

            return FromSubtitle(subtitle, success: false);
        }

        subtitle.OcrStatus = SubtitleOcrStatus.Succeeded;
        subtitle.OcrError = null;
        subtitle.OcrCompletedAt ??= DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _mediaStateService.UpdateStateAsync(context.Media, context.MediaType);

        _logger.LogInformation(
            "Promoted previously blocked OCR for {MediaType} {MediaId} stream {StreamIndex}: quality={QualityScore}, currentThreshold={Threshold}",
            context.MediaType,
            context.Media.Id,
            subtitle.StreamIndex,
            subtitle.OcrQualityScore,
            minimumQualityScore);

        return FromSubtitle(subtitle, success: true);
    }

    private static void NormalizeOcrOutputFile(string outputPath)
    {
        var text = File.ReadAllText(outputPath);
        var normalized = NormalizeCommonOcrTextArtifacts(text);
        if (!string.Equals(text, normalized, StringComparison.Ordinal))
        {
            File.WriteAllText(outputPath, normalized);
        }
    }

    internal static string NormalizeCommonOcrTextArtifacts(string text)
    {
        return Regex.Replace(text, @"(?<![A-Za-z0-9])\|(?![A-Za-z0-9])", "I");
    }

    private async Task<bool> IsOcrEnabledAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await _settingService.GetSetting(SettingKeys.SubtitleExtraction.OcrEnabled);
        return !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<int> GetMinimumQualityScoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await _settingService.GetSetting(SettingKeys.SubtitleExtraction.OcrMinQualityScore);
        return int.TryParse(value, out var score)
            ? Math.Clamp(score, 0, 100)
            : 80;
    }

    private async Task<string> ResolveOcrLanguageAsync(
        EmbeddedSubtitle subtitle,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mode = await _settingService.GetSetting(SettingKeys.SubtitleExtraction.OcrLanguages);
        if (!string.IsNullOrWhiteSpace(mode) &&
            !string.Equals(mode, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return SubtitleOcrLanguageMapper.MapToTesseractLanguage(mode);
        }

        return SubtitleOcrLanguageMapper.MapToTesseractLanguage(subtitle.Language);
    }

    private async Task<EmbeddedSubtitle> MarkFailedAsync(
        OcrMediaContext context,
        string error,
        CancellationToken cancellationToken)
    {
        var subtitle = await SaveCurrentSubtitleStateAsync(
            context,
            context.Subtitle.StreamIndex,
            subtitle =>
            {
                subtitle.OcrStatus = SubtitleOcrStatus.Failed;
                subtitle.OcrError = error;
                subtitle.OcrCompletedAt = DateTime.UtcNow;
            },
            cancellationToken);
        await _mediaStateService.UpdateStateAsync(context.Media, context.MediaType);
        return subtitle;
    }

    private async Task<EmbeddedSubtitle> SaveCurrentSubtitleStateAsync(
        OcrMediaContext context,
        int streamIndex,
        Action<EmbeddedSubtitle> apply,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var subtitle = await LoadCurrentSubtitleAsync(context, streamIndex, cancellationToken)
                           ?? context.Subtitle;
            if (!ReferenceEquals(subtitle, context.Subtitle) &&
                _dbContext.Entry(context.Subtitle).State != EntityState.Detached)
            {
                _dbContext.Entry(context.Subtitle).State = EntityState.Detached;
            }

            apply(subtitle);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return subtitle;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt == 0)
            {
                foreach (var entry in ex.Entries)
                {
                    entry.State = EntityState.Detached;
                }
            }
        }

        var currentSubtitle = await LoadCurrentSubtitleAsync(context, streamIndex, cancellationToken);
        if (currentSubtitle == null)
        {
            throw new DbUpdateConcurrencyException(
                $"Embedded subtitle stream {streamIndex} was replaced during OCR and no current row exists.");
        }

        apply(currentSubtitle);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return currentSubtitle;
    }

    private async Task<EmbeddedSubtitle?> LoadCurrentSubtitleAsync(
        OcrMediaContext context,
        int streamIndex,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.EmbeddedSubtitles
            .Where(subtitle => subtitle.StreamIndex == streamIndex);

        query = context.MediaType == MediaType.Movie
            ? query.Where(subtitle => subtitle.MovieId == context.Media.Id)
            : query.Where(subtitle => subtitle.EpisodeId == context.Media.Id);

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<OcrMediaContext?> LoadContextAsync(
        int mediaId,
        MediaType mediaType,
        int streamIndex,
        CancellationToken cancellationToken)
    {
        if (mediaType == MediaType.Movie)
        {
            var movie = await _dbContext.Movies
                .Include(m => m.EmbeddedSubtitles)
                .FirstOrDefaultAsync(m => m.Id == mediaId, cancellationToken);
            if (movie == null)
            {
                return null;
            }

            var subtitle = movie.EmbeddedSubtitles.FirstOrDefault(s => s.StreamIndex == streamIndex);
            var path = ResolveMediaPath(movie.Path, movie.FileName);
            return subtitle == null || path == null
                ? null
                : new OcrMediaContext(movie, MediaType.Movie, subtitle, path);
        }

        var episode = await _dbContext.Episodes
            .Include(e => e.EmbeddedSubtitles)
            .FirstOrDefaultAsync(e => e.Id == mediaId, cancellationToken);
        if (episode == null)
        {
            return null;
        }

        var episodeSubtitle = episode.EmbeddedSubtitles.FirstOrDefault(s => s.StreamIndex == streamIndex);
        var episodePath = ResolveMediaPath(episode.Path, episode.FileName);
        return episodeSubtitle == null || episodePath == null
            ? null
            : new OcrMediaContext(episode, MediaType.Episode, episodeSubtitle, episodePath);
    }

    private static string? ResolveMediaPath(string? directory, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var direct = Path.Combine(directory, fileName);
        if (File.Exists(direct))
        {
            return direct;
        }

        var mediaExtensions = new[] { ".mkv", ".mp4", ".avi", ".m4v", ".webm", ".mov", ".wmv" };
        var fileNameHasMediaExtension = mediaExtensions.Contains(
            Path.GetExtension(fileName),
            StringComparer.OrdinalIgnoreCase);
        var baseName = fileNameHasMediaExtension
            ? Path.GetFileNameWithoutExtension(fileName)
            : fileName;

        foreach (var extension in mediaExtensions)
        {
            var candidate = Path.Combine(directory, baseName + extension);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static SubtitleOcrResult FromSubtitle(EmbeddedSubtitle subtitle, bool success) => new()
    {
        Success = success,
        Status = subtitle.OcrStatus,
        ExtractedPath = subtitle.OcrExtractedPath,
        Error = subtitle.OcrError,
        CueCount = subtitle.OcrCueCount,
        QualityScore = subtitle.OcrQualityScore,
        IssueSummary = subtitle.OcrIssueSummary
    };

    private static SubtitleOcrResult Fail(SubtitleOcrStatus status, string error) => new()
    {
        Success = false,
        Status = status,
        Error = error
    };

    private sealed record OcrMediaContext(
        IMedia Media,
        MediaType MediaType,
        EmbeddedSubtitle Subtitle,
        string MediaPath);
}
