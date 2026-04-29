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

        var textBasedEmbedded = embeddedSubtitles
            .Where(s => s.IsTextBased)
            .ToList();

        if (textBasedEmbedded.Count == 0)
        {
            return null;
        }

        var allowCaptionFallback = !await ShouldIgnoreCaptionsAsync(cancellationToken);
        var selection = await _subtitleSourceSelectionService.SelectPrimaryAsync(
            textBasedEmbedded,
            configuredSourceLanguages,
            allowCaptionFallback,
            cancellationToken);
        if (selection.SelectedSubtitle == null || string.IsNullOrWhiteSpace(selection.MatchedLanguage))
        {
            return null;
        }

        return CreateEmbeddedSnapshot(selection.SelectedSubtitle, selection.MatchedLanguage);
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
                                       ?? await GetMatchingExternalSubtitlesAsync(media);

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
        var identity =
            $"embedded|{normalizedLanguage}|stream:{subtitle.StreamIndex}|codec:{normalizedCodec}|title:{normalizedTitle}|forced:{subtitle.IsForced}|default:{subtitle.IsDefault}";
        var fingerprint = ComputeMetadataFingerprint(identity, subtitle.StreamIndex, 0);

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
                              tr.SourceSubtitleType != SubtitleLanguageHelper.TypeSignsSongs)))
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

    private async Task<List<Subtitles>> GetMatchingExternalSubtitlesAsync(IMedia media)
    {
        if (string.IsNullOrWhiteSpace(media.Path) || string.IsNullOrWhiteSpace(media.FileName))
        {
            return [];
        }

        try
        {
            var allSubtitles = await _subtitleService.GetAllSubtitles(media.Path);
            return FilterMatchingSubtitles(media.FileName, allSubtitles);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to enumerate subtitles for media {Title}", media.Title);
            return [];
        }
    }

    private static List<Subtitles> FilterMatchingSubtitles(string mediaFileName, IEnumerable<Subtitles> subtitles)
    {
        var mediaNameNoExt = Path.GetFileNameWithoutExtension(mediaFileName);
        return subtitles
            .Where(s =>
                s.FileName.StartsWith(mediaFileName + ".", StringComparison.OrdinalIgnoreCase)
                || s.FileName.Equals(mediaFileName, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(mediaNameNoExt)
                    && s.FileName.StartsWith(mediaNameNoExt + ".", StringComparison.OrdinalIgnoreCase)))
            .ToList();
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
}
