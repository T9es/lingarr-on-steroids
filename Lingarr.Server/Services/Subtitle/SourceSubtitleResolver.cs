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
    private readonly LingarrDbContext _dbContext;
    private readonly ISubtitleExtractionService _subtitleExtractionService;
    private readonly ISourceSubtitleSnapshotService _sourceSubtitleSnapshotService;
    private readonly IEmbeddedSubtitleCacheService _embeddedSubtitleCacheService;
    private readonly ISubtitleSourceSelectionService _subtitleSourceSelectionService;
    private readonly ILogger<SourceSubtitleResolver> _logger;

    public SourceSubtitleResolver(
        LingarrDbContext dbContext,
        ISubtitleService subtitleService,
        ISubtitleExtractionService subtitleExtractionService,
        ISourceSubtitleSnapshotService sourceSubtitleSnapshotService,
        IEmbeddedSubtitleCacheService embeddedSubtitleCacheService,
        ILogger<SourceSubtitleResolver> logger,
        ISubtitleSourceSelectionService? subtitleSourceSelectionService = null)
    {
        _dbContext = dbContext;
        _subtitleExtractionService = subtitleExtractionService;
        _sourceSubtitleSnapshotService = sourceSubtitleSnapshotService;
        _embeddedSubtitleCacheService = embeddedSubtitleCacheService;
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
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
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

        var mediaPath = await ResolveCurrentMediaFilePathAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            _logger.LogDebug(
                "Cannot validate managed embedded subtitle cache {CachePath} because media {MediaId} is unavailable",
                path,
                request.MediaId);
            return false;
        }

        if (_embeddedSubtitleCacheService.IsCurrentForSource(path, mediaPath))
        {
            return true;
        }

        _embeddedSubtitleCacheService.Invalidate(path);
        _logger.LogInformation(
            "Invalidated managed embedded subtitle cache {CachePath} after source media snapshot changed at {MediaPath}",
            path,
            mediaPath);
        return false;
    }

    private async Task<string?> ResolveCurrentMediaFilePathAsync(
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
                .AsNoTracking()
                .Where(item => item.Id == request.MediaId.Value)
                .Select(item => new { item.Path, item.FileName })
                .FirstOrDefaultAsync(cancellationToken);
            return ResolveMediaFilePath(movie?.Path, movie?.FileName);
        }

        if (request.MediaType == MediaType.Episode)
        {
            var episode = await _dbContext.Episodes
                .AsNoTracking()
                .Where(item => item.Id == request.MediaId.Value)
                .Select(item => new { item.Path, item.FileName })
                .FirstOrDefaultAsync(cancellationToken);
            return ResolveMediaFilePath(episode?.Path, episode?.FileName);
        }

        return null;
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

        var mediaExtensions = new[] { ".mkv", ".mp4", ".avi", ".m4v", ".webm", ".mov", ".wmv" };
        var baseName = mediaExtensions.Contains(
            Path.GetExtension(fileName),
            StringComparer.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(fileName)
            : fileName;

        foreach (var extension in mediaExtensions)
        {
            var candidatePath = Path.Combine(directoryPath, baseName + extension);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return Directory.EnumerateFiles(directoryPath)
            .FirstOrDefault(path =>
                mediaExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) &&
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

        if (request.MediaType == MediaType.Movie)
        {
            var movie = await _dbContext.Movies
                .Include(item => item.EmbeddedSubtitles)
                .FirstOrDefaultAsync(item => item.Id == request.MediaId.Value, cancellationToken);
            if (movie == null)
            {
                return [];
            }

            if (movie.EmbeddedSubtitles.Count == 0)
            {
                await _subtitleExtractionService.SyncEmbeddedSubtitles(movie);
                await _dbContext.Entry(movie).Collection(item => item.EmbeddedSubtitles).LoadAsync(cancellationToken);
            }

            var movieSubtitles = movie.EmbeddedSubtitles.ToList();
            await InvalidateStaleManagedArtifactsAsync(request, movieSubtitles, cancellationToken);
            return movieSubtitles;
        }

        var episode = await _dbContext.Episodes
            .Include(item => item.EmbeddedSubtitles)
            .FirstOrDefaultAsync(item => item.Id == request.MediaId.Value, cancellationToken);
        if (episode == null)
        {
            return [];
        }

        if (episode.EmbeddedSubtitles.Count == 0)
        {
            await _subtitleExtractionService.SyncEmbeddedSubtitles(episode);
            await _dbContext.Entry(episode).Collection(item => item.EmbeddedSubtitles).LoadAsync(cancellationToken);
        }

        var episodeSubtitles = episode.EmbeddedSubtitles.ToList();
        await InvalidateStaleManagedArtifactsAsync(request, episodeSubtitles, cancellationToken);
        return episodeSubtitles;
    }

    private async Task InvalidateStaleManagedArtifactsAsync(
        TranslationRequest request,
        IReadOnlyCollection<EmbeddedSubtitle> subtitles,
        CancellationToken cancellationToken)
    {
        if (subtitles.Count == 0)
        {
            return;
        }

        var mediaPath = await ResolveCurrentMediaFilePathAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(mediaPath))
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
