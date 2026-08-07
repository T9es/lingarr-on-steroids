using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.Subtitle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lingarr.Server.Services.Subtitle;

public class SourceSubtitleResolver : ISourceSubtitleResolver
{
    private static readonly string[] MediaExtensions =
        [".mkv", ".mp4", ".avi", ".m4v", ".webm", ".mov", ".wmv"];

    private readonly LingarrDbContext _dbContext;
    private readonly ISubtitleExtractionService _subtitleExtractionService;
    private readonly ISourceSubtitleSnapshotService _sourceSubtitleSnapshotService;
    private readonly IEmbeddedSubtitleCacheService _embeddedSubtitleCacheService;
    private readonly ISubtitleOcrService _subtitleOcrService;
    private readonly ISubtitleSourceSelectionService _subtitleSourceSelectionService;
    private readonly ILogger<SourceSubtitleResolver> _logger;

    public SourceSubtitleResolver(
        LingarrDbContext dbContext,
        ISubtitleService subtitleService,
        ISubtitleExtractionService subtitleExtractionService,
        ISourceSubtitleSnapshotService sourceSubtitleSnapshotService,
        IEmbeddedSubtitleCacheService embeddedSubtitleCacheService,
        ISubtitleOcrService subtitleOcrService,
        ILogger<SourceSubtitleResolver> logger,
        ISubtitleSourceSelectionService? subtitleSourceSelectionService = null)
    {
        _dbContext = dbContext;
        _subtitleExtractionService = subtitleExtractionService;
        _sourceSubtitleSnapshotService = sourceSubtitleSnapshotService;
        _embeddedSubtitleCacheService = embeddedSubtitleCacheService;
        _subtitleOcrService = subtitleOcrService;
        _subtitleSourceSelectionService = subtitleSourceSelectionService ??
            new SubtitleSourceSelectionService(
                subtitleService,
                NullLogger<SubtitleSourceSelectionService>.Instance);
        _logger = logger;
    }

    public async Task<string?> ResolveReadableSourcePathAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (await HasUsableExistingPathAsync(request, cancellationToken))
        {
            if (_embeddedSubtitleCacheService.IsManagedCachePath(request.SubtitleToTranslate))
            {
                _embeddedSubtitleCacheService.Touch(request.SubtitleToTranslate!);
            }

            return request.SubtitleToTranslate;
        }

        if (!request.MediaId.HasValue)
        {
            return null;
        }

        if (string.Equals(request.SourceSnapshotType, SourceSubtitleSnapshot.ExternalType, StringComparison.Ordinal))
        {
            return null;
        }

        var preferredStreamIndex = await ResolvePreferredStreamIndexAsync(request, cancellationToken);
        var extractedPath = await _subtitleExtractionService.TryExtractEmbeddedSubtitleForRequestAsync(
            request.MediaId.Value,
            request.MediaType,
            request.SourceLanguage,
            null,
            preferredStreamIndex);

        if (string.IsNullOrWhiteSpace(extractedPath) || !File.Exists(extractedPath))
        {
            return null;
        }

        request.SubtitleToTranslate = extractedPath;
        request.SourceSubtitleFormat = SubtitleOutputModeHelper.NormalizeFormat(Path.GetExtension(extractedPath));
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Resolved source subtitle path for request {RequestId}: {Path}",
            request.Id,
            extractedPath);

        return extractedPath;
    }

    private async Task<bool> HasUsableExistingPathAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        var path = request.SubtitleToTranslate;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (!File.Exists(path))
        {
            if (_embeddedSubtitleCacheService.IsManagedCachePath(path))
            {
                return await TryRestoreMissingManagedOcrAsync(request, path, cancellationToken);
            }

            return false;
        }

        if (!_embeddedSubtitleCacheService.IsManagedCachePath(path))
        {
            return true;
        }

        if (_embeddedSubtitleCacheService.IsExpired(path) || !request.MediaId.HasValue)
        {
            return false;
        }

        var mediaFile = await ResolveCurrentMediaFileAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(mediaFile.Path))
        {
            _logger.LogDebug(
                "Cannot validate managed embedded subtitle cache {CachePath} because media {MediaId} is unavailable",
                path,
                request.MediaId);
            return false;
        }

        if (_embeddedSubtitleCacheService.IsCurrentForSource(path, mediaFile.Path))
        {
            return true;
        }

        _embeddedSubtitleCacheService.Invalidate(path);
        _logger.LogInformation(
            "Invalidated managed embedded subtitle cache {CachePath} after source media snapshot changed at {MediaPath}",
            path,
            mediaFile.Path);
        return false;
    }

    /// <summary>
    /// Recovers a missing managed OCR cache file whose source media is unchanged by
    /// re-running OCR for the stream (the same invocation the OcrPending flow uses) and
    /// returning the regenerated path. Stale OCR of a CHANGED media is never regenerated:
    /// recovery only fires when the recorded source snapshot still matches the media.
    /// Any failure falls back to the existing behavior (false) - recovery never loops,
    /// never throws, and never masks the original resolution failure.
    /// </summary>
    private async Task<bool> TryRestoreMissingManagedOcrAsync(
        TranslationRequest request,
        string cachePath,
        CancellationToken cancellationToken)
    {
        if (!request.MediaId.HasValue)
        {
            return false;
        }

        var subtitle = await _dbContext.EmbeddedSubtitles
            .FirstOrDefaultAsync(
                item =>
                    item.OcrExtractedPath == cachePath &&
                    (request.MediaType == MediaType.Movie
                        ? item.MovieId == request.MediaId.Value
                        : item.EpisodeId == request.MediaId.Value),
                cancellationToken);
        if (subtitle == null ||
            subtitle.OcrStatus is not (SubtitleOcrStatus.Succeeded or SubtitleOcrStatus.Approved))
        {
            return false;
        }

        var mediaFile = await ResolveCurrentMediaFileAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(mediaFile.Path))
        {
            _logger.LogDebug(
                "Cannot restore missing OCR cache {CachePath} because media {MediaId} is unavailable",
                cachePath,
                request.MediaId);
            return false;
        }

        if (!_embeddedSubtitleCacheService.IsSourceSnapshotCurrent(cachePath, mediaFile.Path))
        {
            return false;
        }

        _logger.LogInformation(
            "OCR cache file missing for {CachePath} but media unchanged; re-running OCR for {MediaType} {MediaId} stream {StreamIndex}",
            cachePath,
            request.MediaType,
            request.MediaId,
            subtitle.StreamIndex);

        // Capture the row state BEFORE re-running OCR: a single failed re-OCR must not be
        // allowed to permanently demote a previously successful row into a terminal
        // status, or the recovery guard would close off all future recovery attempts.
        var capturedOcrStatus = subtitle.OcrStatus;
        var capturedOcrError = subtitle.OcrError;

        try
        {
            var result = await _subtitleOcrService.RunOcrAsync(
                request.MediaId.Value,
                request.MediaType,
                subtitle.StreamIndex,
                manual: false,
                cancellationToken);
            if (!result.Success ||
                string.IsNullOrWhiteSpace(result.ExtractedPath) ||
                !File.Exists(result.ExtractedPath))
            {
                await RestoreOcrRowAfterFailedRecoveryAsync(
                    subtitle,
                    capturedOcrStatus,
                    capturedOcrError,
                    cachePath,
                    cancellationToken);
                return false;
            }

            request.SubtitleToTranslate = result.ExtractedPath;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to re-run OCR for missing cache {CachePath} of {MediaType} {MediaId}; falling back to existing resolution behavior",
                cachePath,
                request.MediaType,
                request.MediaId);
            await RestoreOcrRowAfterFailedRecoveryAsync(
                subtitle,
                capturedOcrStatus,
                capturedOcrError,
                cachePath,
                cancellationToken);
            return false;
        }
    }

    /// <summary>
    /// Reverts a failed re-OCR that demoted a previously successful OCR row into a
    /// terminal status (Failed / BlockedLowQuality). One failed attempt must not lock the
    /// row out of future recovery: the captured Succeeded/Approved state is restored so
    /// the next resolution attempt can re-run OCR again. Best-effort by design: the
    /// recovery path never throws, so a restore failure is logged and otherwise ignored.
    /// </summary>
    private async Task RestoreOcrRowAfterFailedRecoveryAsync(
        EmbeddedSubtitle subtitle,
        SubtitleOcrStatus capturedStatus,
        string? capturedError,
        string cachePath,
        CancellationToken cancellationToken)
    {
        if (capturedStatus is not (SubtitleOcrStatus.Succeeded or SubtitleOcrStatus.Approved))
        {
            return;
        }

        try
        {
            // Reload by Id: RunOcrAsync may have detached or replaced the tracked row
            // while applying its failure state.
            var current = await _dbContext.EmbeddedSubtitles
                .FirstOrDefaultAsync(item => item.Id == subtitle.Id, cancellationToken);
            if (current == null ||
                current.OcrStatus == capturedStatus ||
                current.OcrStatus is not (SubtitleOcrStatus.Failed or SubtitleOcrStatus.BlockedLowQuality))
            {
                return;
            }

            var demotedStatus = current.OcrStatus;
            current.OcrStatus = capturedStatus;
            current.OcrError = capturedError;
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Failed re-OCR for missing cache {CachePath} demoted subtitle {SubtitleId} to {DemotedStatus}; restored to {RestoredStatus} so future recovery attempts remain possible",
                cachePath,
                current.Id,
                demotedStatus,
                capturedStatus);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not restore OCR row state after failed re-OCR for missing cache {CachePath}",
                cachePath);
        }
    }

    private async Task<ResolvedMediaFile> ResolveCurrentMediaFileAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.MediaId.HasValue)
        {
            return ResolvedMediaFile.None;
        }

        if (request.MediaType == MediaType.Movie)
        {
            var movie = await _dbContext.Movies
                .FirstOrDefaultAsync(item => item.Id == request.MediaId.Value, cancellationToken);
            if (movie == null)
            {
                return ResolvedMediaFile.None;
            }

            return await ResolveMediaFileWithAdoptionAsync(
                movie.Path,
                movie.FileName,
                adoptedFileName =>
                {
                    movie.FileName = adoptedFileName;
                    return _dbContext.SaveChangesAsync(cancellationToken);
                });
        }

        if (request.MediaType == MediaType.Episode)
        {
            var episode = await _dbContext.Episodes
                .FirstOrDefaultAsync(item => item.Id == request.MediaId.Value, cancellationToken);
            if (episode == null)
            {
                return ResolvedMediaFile.None;
            }

            return await ResolveMediaFileWithAdoptionAsync(
                episode.Path,
                episode.FileName,
                adoptedFileName =>
                {
                    episode.FileName = adoptedFileName;
                    return _dbContext.SaveChangesAsync(cancellationToken);
                });
        }

        return ResolvedMediaFile.None;
    }

    /// <summary>
    /// Resolves the media file for a directory + DB file name. When the recorded file name is
    /// stale (the release was replaced on disk) or missing, and the directory holds EXACTLY ONE
    /// video file, that file is adopted: the DB file name is updated and the resolution continues
    /// with it. With zero or multiple video files we never guess - the resolution fails exactly
    /// as it would have before.
    /// </summary>
    private static async Task<ResolvedMediaFile> ResolveMediaFileWithAdoptionAsync(
        string? directoryPath,
        string? fileName,
        Func<string, Task> persistAdoption)
    {
        var directPath = ResolveMediaFilePath(directoryPath, fileName);
        if (directPath != null)
        {
            return new ResolvedMediaFile(directPath, FileNameChanged: false);
        }

        var searchDirectory = directoryPath;
        if (string.IsNullOrWhiteSpace(searchDirectory) &&
            !string.IsNullOrWhiteSpace(fileName) &&
            Path.IsPathRooted(fileName))
        {
            searchDirectory = Path.GetDirectoryName(fileName);
        }

        var videoFiles = EnumerateVideoFiles(searchDirectory);
        if (videoFiles.Count != 1)
        {
            return ResolvedMediaFile.None;
        }

        var adoptedFileName = Path.GetFileName(videoFiles[0]);
        var fileNameChanged = !string.Equals(
            adoptedFileName,
            fileName,
            StringComparison.OrdinalIgnoreCase);
        if (fileNameChanged)
        {
            await persistAdoption(adoptedFileName);
        }

        return new ResolvedMediaFile(videoFiles[0], fileNameChanged);
    }

    private static List<string> EnumerateVideoFiles(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(directoryPath)
                .Where(path => MediaExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private sealed record ResolvedMediaFile(string? Path, bool FileNameChanged)
    {
        public static ResolvedMediaFile None { get; } = new(null, false);
    }

    private static string? ResolveMediaFilePath(string? directoryPath, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        if (Path.IsPathRooted(fileName))
        {
            return File.Exists(fileName) ? fileName : null;
        }

        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return null;
        }

        var directPath = Path.Combine(directoryPath, fileName);
        if (File.Exists(directPath))
        {
            return directPath;
        }

        if (!Directory.Exists(directoryPath))
        {
            return null;
        }

        var baseName = MediaExtensions.Contains(
            Path.GetExtension(fileName),
            StringComparer.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(fileName)
            : fileName;

        foreach (var extension in MediaExtensions)
        {
            var candidatePath = Path.Combine(directoryPath, baseName + extension);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return Directory.EnumerateFiles(directoryPath)
            .FirstOrDefault(path =>
                MediaExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) &&
                (string.Equals(
                     Path.GetFileNameWithoutExtension(path),
                     baseName,
                     StringComparison.OrdinalIgnoreCase) ||
                 Path.GetFileName(path).StartsWith(
                     baseName + ".",
                     StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<int?> ResolvePreferredStreamIndexAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        var embeddedSubtitles = await LoadCurrentEmbeddedSubtitlesAsync(request, cancellationToken);
        if (embeddedSubtitles.Count == 0)
        {
            return request.SourceSnapshotStreamIndex;
        }

        if (request.SourceSnapshotStreamIndex.HasValue)
        {
            var exactStream = embeddedSubtitles.FirstOrDefault(subtitle =>
                subtitle.IsReadableSource() && subtitle.StreamIndex == request.SourceSnapshotStreamIndex.Value);
            if (exactStream != null)
            {
                return exactStream.StreamIndex;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.SourceSnapshotIdentity))
        {
            foreach (var subtitle in embeddedSubtitles.Where(subtitle => subtitle.IsReadableSource()))
            {
                var snapshot = _sourceSubtitleSnapshotService.CreateEmbeddedSnapshot(subtitle, request.SourceLanguage);
                if (string.Equals(snapshot.Identity, request.SourceSnapshotIdentity, StringComparison.Ordinal))
                {
                    return subtitle.StreamIndex;
                }
            }
        }

        var readableSubtitles = embeddedSubtitles
            .Where(subtitle => subtitle.IsReadableSource())
            .ToList();
        if (!SubtitleLanguageHelper.IsSupplementalSubtitleType(request.SourceSubtitleType))
        {
            var selection = await _subtitleSourceSelectionService.SelectPrimaryAsync(
                readableSubtitles,
                [request.SourceLanguage],
                allowCaptionFallback: true,
                cancellationToken: cancellationToken);
            if (selection.SelectedSubtitle != null)
            {
                return selection.SelectedSubtitle.StreamIndex;
            }
        }

        return readableSubtitles
            .OrderByDescending(subtitle => ScoreSubtitle(subtitle, request))
            .Select(subtitle => (int?)subtitle.StreamIndex)
            .FirstOrDefault();
    }

    private async Task<List<EmbeddedSubtitle>> LoadCurrentEmbeddedSubtitlesAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.MediaId.HasValue)
        {
            return [];
        }

        var mediaFile = await ResolveCurrentMediaFileAsync(request, cancellationToken);

        if (request.MediaType == MediaType.Movie)
        {
            var movie = await _dbContext.Movies
                .Include(item => item.EmbeddedSubtitles)
                .FirstOrDefaultAsync(item => item.Id == request.MediaId.Value, cancellationToken);
            if (movie == null)
            {
                return [];
            }

            // Refresh when there are no rows at all, or when the media file changed on disk
            // (the DB file name was stale and the resolver adopted the replacement file) -
            // rows probed from the old release are no longer accurate.
            if (movie.EmbeddedSubtitles.Count == 0 || mediaFile.FileNameChanged)
            {
                await _subtitleExtractionService.SyncEmbeddedSubtitles(movie);
                await _dbContext.Entry(movie).Collection(item => item.EmbeddedSubtitles).LoadAsync(cancellationToken);
            }

            var movieSubtitles = movie.EmbeddedSubtitles.ToList();
            await InvalidateStaleManagedArtifactsAsync(movieSubtitles, mediaFile.Path, cancellationToken);
            return movieSubtitles;
        }

        var episode = await _dbContext.Episodes
            .Include(item => item.EmbeddedSubtitles)
            .FirstOrDefaultAsync(item => item.Id == request.MediaId.Value, cancellationToken);
        if (episode == null)
        {
            return [];
        }

        if (episode.EmbeddedSubtitles.Count == 0 || mediaFile.FileNameChanged)
        {
            await _subtitleExtractionService.SyncEmbeddedSubtitles(episode);
            await _dbContext.Entry(episode).Collection(item => item.EmbeddedSubtitles).LoadAsync(cancellationToken);
        }

        var episodeSubtitles = episode.EmbeddedSubtitles.ToList();
        await InvalidateStaleManagedArtifactsAsync(episodeSubtitles, mediaFile.Path, cancellationToken);
        return episodeSubtitles;
    }

    private async Task InvalidateStaleManagedArtifactsAsync(
        IReadOnlyCollection<EmbeddedSubtitle> subtitles,
        string? mediaPath,
        CancellationToken cancellationToken)
    {
        if (subtitles.Count == 0 || string.IsNullOrWhiteSpace(mediaPath))
        {
            return;
        }

        var changed = false;
        foreach (var subtitle in subtitles)
        {
            if (IsStaleManagedArtifact(subtitle.ExtractedPath, mediaPath))
            {
                _embeddedSubtitleCacheService.Invalidate(subtitle.ExtractedPath!);
                subtitle.IsExtracted = false;
                subtitle.ExtractedPath = null;
                changed = true;
            }

            if (IsStaleManagedArtifact(subtitle.OcrExtractedPath, mediaPath))
            {
                _embeddedSubtitleCacheService.Invalidate(subtitle.OcrExtractedPath!);
                SubtitleOcrStatePolicy.Reset(subtitle);
                changed = true;
            }
        }

        if (changed)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private bool IsStaleManagedArtifact(string? path, string mediaPath)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               _embeddedSubtitleCacheService.IsManagedCachePath(path) &&
               !_embeddedSubtitleCacheService.IsCurrentForSource(path, mediaPath);
    }

    private static int ScoreSubtitle(EmbeddedSubtitle subtitle, TranslationRequest request)
    {
        var score = SubtitleLanguageHelper.ScoreSubtitleCandidate(subtitle, request.SourceLanguage);

        if (!string.IsNullOrWhiteSpace(request.SelectedStreamTitle) &&
            string.Equals(subtitle.Title?.Trim(), request.SelectedStreamTitle.Trim(), StringComparison.OrdinalIgnoreCase))
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
        if (!string.IsNullOrWhiteSpace(requestType) &&
            string.Equals(SubtitleLanguageHelper.DetermineSubtitleType(subtitle), requestType, StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        return score;
    }

}
