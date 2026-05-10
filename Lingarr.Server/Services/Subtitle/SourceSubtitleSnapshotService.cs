using System.Security.Cryptography;
using System.Text;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Subtitle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lingarr.Server.Services.Subtitle;

public class SourceSubtitleSnapshotService : ISourceSubtitleSnapshotService
{
    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settingService;
    private readonly ISubtitleService _subtitleService;
    private readonly ISubtitleSourceSelectionService _subtitleSourceSelectionService;
    private readonly ILogger<SourceSubtitleSnapshotService> _logger;

    public SourceSubtitleSnapshotService(
        LingarrDbContext dbContext,
        ISettingService settingService,
        ISubtitleService subtitleService,
        ILogger<SourceSubtitleSnapshotService> logger,
        ISubtitleSourceSelectionService? subtitleSourceSelectionService = null)
    {
        _dbContext = dbContext;
        _settingService = settingService;
        _subtitleService = subtitleService;
        _subtitleSourceSelectionService = subtitleSourceSelectionService ??
            new SubtitleSourceSelectionService(
                subtitleService,
                NullLogger<SubtitleSourceSelectionService>.Instance);
        _logger = logger;
    }

    public async Task<SourceSubtitleSnapshot?> ResolveCurrentSnapshotAsync(
        IMedia media,
        MediaType mediaType,
        IReadOnlyCollection<EmbeddedSubtitle> embeddedSubtitles,
        IReadOnlyCollection<Subtitles>? externalSubtitles = null,
        CancellationToken cancellationToken = default)
    {
        var externalSource = await ResolveExternalSourceAsync(
            media,
            externalSubtitles,
            cancellationToken);

        if (externalSource != null)
        {
            return externalSource.Snapshot;
        }

        var configuredSourceLanguages = await GetConfiguredSourceLanguagesAsync(cancellationToken);
        if (configuredSourceLanguages.Count == 0)
        {
            return null;
        }

        var readableEmbedded = embeddedSubtitles
            .Where(s => s.IsReadableSource())
            .ToList();

        if (readableEmbedded.Count == 0)
        {
            return null;
        }

        var allowCaptionFallback = !await ShouldIgnoreCaptionsAsync(cancellationToken);
        var selection = await _subtitleSourceSelectionService.SelectPrimaryAsync(
            readableEmbedded,
            configuredSourceLanguages,
            allowCaptionFallback,
            cancellationToken: cancellationToken);
        if (selection.SelectedSubtitle == null || string.IsNullOrWhiteSpace(selection.MatchedLanguage))
        {
            return null;
        }

        return CreateEmbeddedSnapshot(selection.SelectedSubtitle, selection.MatchedLanguage);
    }

    public async Task<SourceSubtitleSnapshot?> ResolveCurrentSnapshotWithAutoAsync(
        IMedia media,
        MediaType mediaType,
        IReadOnlyCollection<EmbeddedSubtitle> embeddedSubtitles,
        IReadOnlyCollection<Subtitles>? externalSubtitles,
        bool useAutoMode,
        IReadOnlyList<string>? targetLanguages,
        CancellationToken cancellationToken = default)
    {
        if (!useAutoMode)
        {
            return await ResolveCurrentSnapshotAsync(
                media, mediaType, embeddedSubtitles, externalSubtitles, cancellationToken);
        }

        // In auto mode, bypass configured language filtering — accept all sources
        var externalSource = await ResolveExternalSourceWithAutoAsync(
            media, externalSubtitles, true, targetLanguages, cancellationToken);
        if (externalSource != null)
        {
            return externalSource.Snapshot;
        }

        var readableEmbedded = embeddedSubtitles
            .Where(s => s.IsReadableSource())
            .ToList();

        if (readableEmbedded.Count == 0)
        {
            return null;
        }

        var allowCaptionFallback = !await ShouldIgnoreCaptionsAsync(cancellationToken);
        var selection = await _subtitleSourceSelectionService.SelectPrimaryAsync(
            readableEmbedded,
            [],
            allowCaptionFallback,
            targetLanguages: targetLanguages,
            cancellationToken: cancellationToken);

        if (selection.SelectedSubtitle != null && !string.IsNullOrWhiteSpace(selection.MatchedLanguage))
        {
            return CreateEmbeddedSnapshot(selection.SelectedSubtitle, selection.MatchedLanguage);
        }

        return null;
    }

    public async Task<ResolvedExternalSourceSubtitle?> ResolveExternalSourceAsync(
        IMedia media,
        IReadOnlyCollection<Subtitles>? externalSubtitles = null,
        CancellationToken cancellationToken = default)
    {
        var configuredSourceLanguages = await GetConfiguredSourceLanguagesAsync(cancellationToken);
        if (configuredSourceLanguages.Count == 0)
        {
            return null;
        }

        var ignoreCaptions = await ShouldIgnoreCaptionsAsync(cancellationToken);
        var matchingExternalSubtitles = externalSubtitles?.ToList()
                                       ?? await GetMatchingExternalSubtitlesAsync(media, cancellationToken);

        var externalCandidate = TrySelectExternalSourceCandidate(
            matchingExternalSubtitles,
            configuredSourceLanguages,
            ignoreCaptions);

        if (externalCandidate.Subtitle == null || string.IsNullOrWhiteSpace(externalCandidate.SourceLanguage))
        {
            return null;
        }

        return new ResolvedExternalSourceSubtitle
        {
            Subtitle = externalCandidate.Subtitle,
            SourceLanguage = externalCandidate.SourceLanguage,
            Snapshot = CreateExternalSnapshot(externalCandidate.Subtitle.Path, externalCandidate.SourceLanguage)
        };
    }

    public async Task<ResolvedExternalSourceSubtitle?> ResolveExternalSourceWithAutoAsync(
        IMedia media,
        IReadOnlyCollection<Subtitles>? externalSubtitles,
        bool useAutoMode,
        IReadOnlyList<string>? targetLanguages,
        CancellationToken cancellationToken = default)
    {
        if (!useAutoMode)
        {
            return await ResolveExternalSourceAsync(media, externalSubtitles, cancellationToken);
        }

        var ignoreCaptions = await ShouldIgnoreCaptionsAsync(cancellationToken);
        var matchingExternalSubtitles = externalSubtitles?.ToList()
                                       ?? await GetMatchingExternalSubtitlesAsync(media, cancellationToken);

        // Accept all external subtitles regardless of configured languages
        var autoCandidate = SelectAutoExternalCandidate(matchingExternalSubtitles, ignoreCaptions);
        if (autoCandidate.Subtitle == null || string.IsNullOrWhiteSpace(autoCandidate.SourceLanguage))
        {
            return null;
        }

        return new ResolvedExternalSourceSubtitle
        {
            Subtitle = autoCandidate.Subtitle,
            SourceLanguage = autoCandidate.SourceLanguage,
            Snapshot = CreateExternalSnapshot(autoCandidate.Subtitle.Path, autoCandidate.SourceLanguage)
        };
    }

    /// <summary>
    /// Selects the best external subtitle candidate without filtering by configured source languages.
    /// Used in auto mode when any available external subtitle language is acceptable.
    /// </summary>
    private static (Subtitles? Subtitle, string? SourceLanguage) SelectAutoExternalCandidate(
        List<Subtitles> subtitles,
        bool ignoreCaptions)
    {
        var validSubtitles = subtitles
            .Where(s => !ExternalSubtitleCandidateHelper.ShouldSkipAsPrimarySource(s))
            .Where(s => !ExternalSubtitleCandidateHelper.IsSupplementalOrCommentary(s))
            .ToList();

        if (validSubtitles.Count == 0)
        {
            return (null, null);
        }

        var candidates = new List<(Subtitles Subtitle, string Language, int Score)>();
        foreach (var subtitle in validSubtitles)
        {
            var language = SubtitleLanguageHelper.DetectLanguageFromFileName(subtitle.FileName);
            if (string.IsNullOrWhiteSpace(language) ||
                language.Equals("und", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var score = ExternalSubtitleCandidateHelper.ScorePrimarySourceCandidate(subtitle);
            candidates.Add((subtitle, language, score));
        }

        var cleanCandidate = candidates
            .Where(c => !SubtitleLanguageHelper.IsCaptionSubtitleType(
                ExternalSubtitleCandidateHelper.GetSubtitleType(c.Subtitle)))
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Subtitle.Path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (cleanCandidate.Subtitle != null)
        {
            return (cleanCandidate.Subtitle, cleanCandidate.Language);
        }

        if (!ignoreCaptions)
        {
            var captionCandidate = candidates
                .Where(c => SubtitleLanguageHelper.IsCaptionSubtitleType(
                    ExternalSubtitleCandidateHelper.GetSubtitleType(c.Subtitle)))
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.Subtitle.Path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (captionCandidate.Subtitle != null)
            {
                return (captionCandidate.Subtitle, captionCandidate.Language);
            }
        }

        return (null, null);
    }

    public SourceSubtitleSnapshot CreateExternalSnapshot(string subtitlePath, string sourceLanguage)
    {
        var normalizedLanguage = SubtitleLanguageHelper.NormalizeLanguageCode(sourceLanguage);
        var normalizedPath = NormalizePath(subtitlePath);

        long? fileSize = null;
        DateTime? lastWriteUtc = null;
        string? contentHash = null;
        try
        {
            var info = new FileInfo(subtitlePath);
            if (info.Exists)
            {
                fileSize = info.Length;
                lastWriteUtc = info.LastWriteTimeUtc;
                using var stream = info.OpenRead();
                contentHash = Convert.ToHexString(SHA256.HashData(stream));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read file metadata for source subtitle snapshot: {Path}", subtitlePath);
        }

        var identity = $"external|{normalizedLanguage}|{normalizedPath}";
        var fingerprint = !string.IsNullOrWhiteSpace(contentHash)
            ? ComputeStringFingerprint($"{identity}|content:{contentHash}|v{SourceSubtitleSnapshot.CurrentVersion}")
            : ComputeMetadataFingerprint(identity, fileSize, lastWriteUtc?.Ticks);

        return new SourceSubtitleSnapshot
        {
            Version = SourceSubtitleSnapshot.CurrentVersion,
            SourceType = SourceSubtitleSnapshot.ExternalType,
            SourceLanguage = normalizedLanguage,
            Identity = identity,
            Fingerprint = fingerprint,
            SourcePath = normalizedPath,
            FileSizeBytes = fileSize,
            LastWriteUtc = lastWriteUtc
        };
    }

    public SourceSubtitleSnapshot CreateEmbeddedSnapshot(EmbeddedSubtitle subtitle, string sourceLanguage)
    {
        var normalizedLanguage = SubtitleLanguageHelper.NormalizeLanguageCode(sourceLanguage);
        var normalizedTitle = (subtitle.Title ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedCodec = (subtitle.CodecName ?? string.Empty).Trim().ToLowerInvariant();
        var ocrMarker = subtitle.HasUsableOcr()
            ? $"|ocr:{subtitle.OcrStatus}|ocrquality:{subtitle.OcrQualityScore}|ocrcues:{subtitle.OcrCueCount}"
            : string.Empty;
        var identity =
            $"embedded|{normalizedLanguage}|stream:{subtitle.StreamIndex}|codec:{normalizedCodec}|title:{normalizedTitle}|forced:{subtitle.IsForced}|default:{subtitle.IsDefault}{ocrMarker}";
        var fingerprint = subtitle.HasUsableOcr()
            ? ComputeOcrFingerprint(identity, subtitle.OcrExtractedPath)
            : ComputeMetadataFingerprint(identity, subtitle.StreamIndex, 0);

        return new SourceSubtitleSnapshot
        {
            Version = SourceSubtitleSnapshot.CurrentVersion,
            SourceType = SourceSubtitleSnapshot.EmbeddedType,
            SourceLanguage = normalizedLanguage,
            Identity = identity,
            Fingerprint = fingerprint,
            StreamIndex = subtitle.StreamIndex
        };
    }

    public bool IsRequestStaleForSnapshot(TranslationRequest request, SourceSubtitleSnapshot currentSnapshot)
    {
        if (!string.IsNullOrWhiteSpace(request.SourceSnapshotFingerprint))
        {
            if (request.SourceSnapshotVersion != currentSnapshot.Version)
            {
                return true;
            }

            return !string.Equals(
                request.SourceSnapshotFingerprint,
                currentSnapshot.Fingerprint,
                StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(request.SourceSnapshotIdentity))
        {
            return !string.Equals(
                request.SourceSnapshotIdentity,
                currentSnapshot.Identity,
                StringComparison.Ordinal);
        }

        // Backward compatibility for requests created before snapshot fields existed.
        if (currentSnapshot.SourceType == SourceSubtitleSnapshot.ExternalType)
        {
            if (!string.IsNullOrWhiteSpace(request.SubtitleToTranslate))
            {
                var requestPath = NormalizePath(request.SubtitleToTranslate);
                if (!string.IsNullOrWhiteSpace(currentSnapshot.SourcePath) &&
                    !string.Equals(requestPath, currentSnapshot.SourcePath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (request.CompletedAt.HasValue && currentSnapshot.LastWriteUtc.HasValue)
            {
                // Allow small timestamp jitter between filesystem and db writes.
                if (currentSnapshot.LastWriteUtc.Value > request.CompletedAt.Value.AddSeconds(1))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public async Task<HashSet<string>> GetStaleTargetLanguagesAsync(
        int mediaId,
        MediaType mediaType,
        IEnumerable<string> targetLanguages,
        SourceSubtitleSnapshot? currentSnapshot,
        CancellationToken cancellationToken = default)
    {
        var staleTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (currentSnapshot == null)
        {
            return staleTargets;
        }

        var normalizedTargets = targetLanguages
            .Select(SubtitleLanguageHelper.NormalizeLanguageCode)
            .Where(lang => !string.IsNullOrWhiteSpace(lang))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedTargets.Count == 0)
        {
            return staleTargets;
        }

        var requiredOutputFormats = await ResolveRequiredOutputFormatsAsync(currentSnapshot, cancellationToken);
        if (requiredOutputFormats.Count == 0)
        {
            return staleTargets;
        }

        var requests = await _dbContext.TranslationRequests
            .AsNoTracking()
            .Where(tr => tr.WorkloadKind == TranslationWorkloadKind.Library
                         && tr.MediaId == mediaId
                         && tr.MediaType == mediaType
                         && tr.Status == TranslationStatus.Completed
                         && tr.SourceDedupeKey == "primary"
                         && (tr.SourceSubtitleType == null ||
                             (tr.SourceSubtitleType != SubtitleLanguageHelper.TypeForced &&
                              tr.SourceSubtitleType != SubtitleLanguageHelper.TypeSignsSongs &&
                              tr.SourceSubtitleType != SubtitleLanguageHelper.TypeForcedDialogue)))
            .OrderByDescending(tr => tr.CompletedAt)
            .ThenByDescending(tr => tr.Id)
            .ToListAsync(cancellationToken);

        foreach (var normalizedTarget in normalizedTargets)
        {
            var remainingFormats = new HashSet<string>(requiredOutputFormats, StringComparer.OrdinalIgnoreCase);
            foreach (var request in requests)
            {
                var requestTarget = SubtitleLanguageHelper.NormalizeLanguageCode(request.TargetLanguage);
                if (!string.Equals(requestTarget, normalizedTarget, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var coveredFormats = GetRequestCoveredFormats(request);
                coveredFormats.IntersectWith(remainingFormats);
                if (coveredFormats.Count == 0)
                {
                    continue;
                }

                if (IsRequestStaleForSnapshot(request, currentSnapshot))
                {
                    staleTargets.Add(normalizedTarget);
                    break;
                }

                remainingFormats.ExceptWith(coveredFormats);
                if (remainingFormats.Count == 0)
                {
                    break;
                }
            }
        }

        return staleTargets;
    }

    private async Task<List<string>> GetConfiguredSourceLanguagesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var sourceLanguages =
                await _settingService.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages);
            return sourceLanguages
                .Select(l => SubtitleLanguageHelper.NormalizeLanguageCode(l.Code))
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private async Task<bool> ShouldIgnoreCaptionsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ignoreCaptions = await _settingService.GetSetting(SettingKeys.Translation.IgnoreCaptions);
        return string.Equals(ignoreCaptions, "true", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<List<Subtitles>> GetMatchingExternalSubtitlesAsync(
        IMedia media,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(media.Path) || string.IsNullOrWhiteSpace(media.FileName))
        {
            return [];
        }

        try
        {
            var allSubtitles = await _subtitleService.GetAllSubtitles(media.Path);
            return MediaSubtitleMatcher.FilterMatchingSubtitles(
                media.FileName,
                allSubtitles,
                await GetKnownGeneratedSubtitlePathsAsync(media.Id, ResolveMediaType(media), cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to enumerate subtitles for media {Title}", media.Title);
            return [];
        }
    }

    private static MediaType ResolveMediaType(IMedia media)
    {
        return media is Movie ? MediaType.Movie : MediaType.Episode;
    }

    private async Task<HashSet<string>> GetKnownGeneratedSubtitlePathsAsync(
        int mediaId,
        MediaType mediaType,
        CancellationToken cancellationToken)
    {
        var requests = await _dbContext.TranslationRequests
            .AsNoTracking()
            .Where(request => request.WorkloadKind == TranslationWorkloadKind.Library)
            .Where(request => request.MediaId == mediaId && request.MediaType == mediaType)
            .Where(request => request.Status == TranslationStatus.Completed)
            .Where(request => request.GeneratedSubtitlePaths != null && request.GeneratedSubtitlePaths != string.Empty)
            .ToListAsync(cancellationToken);

        return MediaSubtitleMatcher.ExtractGeneratedPaths(requests);
    }

    private static (Subtitles? Subtitle, string? SourceLanguage) TrySelectExternalSourceCandidate(
        List<Subtitles> subtitles,
        List<string> configuredSourceLanguages,
        bool ignoreCaptions)
    {
        var selection = ExternalSubtitleCandidateHelper.SelectPrimarySourceCandidate(
            subtitles,
            configuredSourceLanguages,
            ignoreCaptions);
        return (selection?.Subtitle, selection?.SourceLanguage);
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).Trim().ToLowerInvariant();
        }
        catch
        {
            return path.Trim().ToLowerInvariant();
        }
    }

    private static string ComputeMetadataFingerprint(string identity, long? first, long? second)
    {
        var input = $"{identity}|{first?.ToString() ?? "null"}|{second?.ToString() ?? "null"}|v{SourceSubtitleSnapshot.CurrentVersion}";
        return ComputeStringFingerprint(input);
    }

    private async Task<HashSet<string>> ResolveRequiredOutputFormatsAsync(
        SourceSubtitleSnapshot currentSnapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var subtitleOutputMode = SubtitleOutputModeHelper.Parse(
            await _settingService.GetSetting(SettingKeys.Translation.SubtitleOutputMode));
        var sourceFormat = ResolveSourceFormat(currentSnapshot);
        return SubtitleOutputModeHelper.GetRequiredOutputFormats(sourceFormat, subtitleOutputMode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<int, int>> BuildEmbeddedContentScoreAdjustmentsAsync(
        IReadOnlyCollection<EmbeddedSubtitle> subtitles,
        CancellationToken cancellationToken)
    {
        var adjustments = new Dictionary<int, int>();

        foreach (var subtitle in subtitles)
        {
            if (string.IsNullOrWhiteSpace(subtitle.ExtractedPath) || !File.Exists(subtitle.ExtractedPath))
            {
                continue;
            }

            try
            {
                var analysis = await AssSubtitleSourceAnalyzer.AnalyzeExtractedSubtitleAsync(
                    subtitle,
                    _subtitleService,
                    cancellationToken);
                if (analysis == null)
                {
                    continue;
                }

                adjustments[subtitle.StreamIndex] = analysis.ContentScoreAdjustment;
                if (analysis.IsPathological)
                {
                    _logger.LogWarning(
                        "Embedded subtitle stream {StreamIndex} ({Title}) looks pathological: drawingEvents={DrawingEvents}, translatableEvents={TranslatableEvents}, duplicateRatio={DuplicateRatio:F2}, avgProviderChars={AverageChars:F2}",
                        subtitle.StreamIndex,
                        subtitle.Title ?? subtitle.CodecName,
                        analysis.DrawingEvents,
                        analysis.TranslatableEvents,
                        analysis.DuplicateRatio,
                        analysis.AverageProviderCharsPerTranslatableCue);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Failed to analyze embedded subtitle stream {StreamIndex} at {ExtractedPath}. Falling back to title-based scoring only.",
                    subtitle.StreamIndex,
                    subtitle.ExtractedPath);
            }
        }

        return adjustments;
    }

    private static HashSet<string> GetRequestCoveredFormats(TranslationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.GeneratedOutputFormats))
        {
            return SubtitleOutputModeHelper.DeserializeFormats(request.GeneratedOutputFormats)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(request.RequiredOutputFormats))
        {
            return SubtitleOutputModeHelper.DeserializeFormats(request.RequiredOutputFormats)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(request.TranslatedSubtitle))
        {
            return [SubtitleOutputModeHelper.NormalizeFormat(Path.GetExtension(request.TranslatedSubtitle))];
        }

        var sourceFormat = SubtitleOutputModeHelper.NormalizeFormat(
            request.SourceSubtitleFormat ?? Path.GetExtension(request.SubtitleToTranslate));
        var subtitleOutputMode = SubtitleOutputModeHelper.Parse(request.SubtitleOutputMode);
        return SubtitleOutputModeHelper.GetRequiredOutputFormats(sourceFormat, subtitleOutputMode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveSourceFormat(SourceSubtitleSnapshot currentSnapshot)
    {
        if (currentSnapshot.SourceType == SourceSubtitleSnapshot.ExternalType)
        {
            return SubtitleOutputModeHelper.NormalizeFormat(Path.GetExtension(currentSnapshot.SourcePath));
        }

        var codecMarker = "|codec:";
        if (currentSnapshot.Identity.Contains("|ocr:", StringComparison.OrdinalIgnoreCase))
        {
            return SubtitleOutputModeHelper.NormalizeFormat(".srt");
        }

        var markerIndex = currentSnapshot.Identity.IndexOf(codecMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            var codecStart = markerIndex + codecMarker.Length;
            var codecEnd = currentSnapshot.Identity.IndexOf('|', codecStart);
            var codec = codecEnd >= 0
                ? currentSnapshot.Identity[codecStart..codecEnd]
                : currentSnapshot.Identity[codecStart..];
            return SubtitleOutputModeHelper.NormalizeFormat(codec);
        }

        return SubtitleOutputModeHelper.NormalizeFormat(".srt");
    }

    private static string ComputeStringFingerprint(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    private static string ComputeOcrFingerprint(string identity, string? ocrPath)
    {
        if (string.IsNullOrWhiteSpace(ocrPath) || !File.Exists(ocrPath))
        {
            return ComputeMetadataFingerprint(identity, null, null);
        }

        try
        {
            using var stream = File.OpenRead(ocrPath);
            var contentHash = Convert.ToHexString(SHA256.HashData(stream));
            return ComputeStringFingerprint($"{identity}|content:{contentHash}|v{SourceSubtitleSnapshot.CurrentVersion}");
        }
        catch
        {
            try
            {
                var info = new FileInfo(ocrPath);
                return ComputeMetadataFingerprint(
                    identity,
                    info.Exists ? info.Length : null,
                    info.Exists ? info.LastWriteTimeUtc.Ticks : null);
            }
            catch
            {
                return ComputeMetadataFingerprint(identity, null, null);
            }
        }
    }
}
