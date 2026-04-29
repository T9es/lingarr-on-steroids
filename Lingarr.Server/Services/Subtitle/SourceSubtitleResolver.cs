using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.Subtitle;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.Subtitle;

public class SourceSubtitleResolver : ISourceSubtitleResolver
{
    private readonly LingarrDbContext _dbContext;
    private readonly ISubtitleExtractionService _subtitleExtractionService;
    private readonly ISourceSubtitleSnapshotService _sourceSubtitleSnapshotService;
    private readonly IEmbeddedSubtitleCacheService _embeddedSubtitleCacheService;
    private readonly ILogger<SourceSubtitleResolver> _logger;

    public SourceSubtitleResolver(
        LingarrDbContext dbContext,
        ISubtitleExtractionService subtitleExtractionService,
        ISourceSubtitleSnapshotService sourceSubtitleSnapshotService,
        IEmbeddedSubtitleCacheService embeddedSubtitleCacheService,
        ILogger<SourceSubtitleResolver> logger)
    {
        _dbContext = dbContext;
        _subtitleExtractionService = subtitleExtractionService;
        _sourceSubtitleSnapshotService = sourceSubtitleSnapshotService;
        _embeddedSubtitleCacheService = embeddedSubtitleCacheService;
        _logger = logger;
    }

    public async Task<string?> ResolveReadableSourcePathAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (HasUsableExistingPath(request.SubtitleToTranslate))
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

    private bool HasUsableExistingPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        if (!_embeddedSubtitleCacheService.IsManagedCachePath(path))
        {
            return true;
        }

        return !_embeddedSubtitleCacheService.IsExpired(path);
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
                subtitle.IsTextBased && subtitle.StreamIndex == request.SourceSnapshotStreamIndex.Value);
            if (exactStream != null)
            {
                return exactStream.StreamIndex;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.SourceSnapshotIdentity))
        {
            foreach (var subtitle in embeddedSubtitles.Where(subtitle => subtitle.IsTextBased))
            {
                var snapshot = _sourceSubtitleSnapshotService.CreateEmbeddedSnapshot(subtitle, request.SourceLanguage);
                if (string.Equals(snapshot.Identity, request.SourceSnapshotIdentity, StringComparison.Ordinal))
                {
                    return subtitle.StreamIndex;
                }
            }
        }

        return embeddedSubtitles
            .Where(subtitle => subtitle.IsTextBased)
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

            return movie.EmbeddedSubtitles.ToList();
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

        return episode.EmbeddedSubtitles.ToList();
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
