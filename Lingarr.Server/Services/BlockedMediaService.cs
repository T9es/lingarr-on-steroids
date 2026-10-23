using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Services.Media;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

/// <summary>
/// Queries media items that are blocked from translation so they can be
/// surfaced in the UI: OCR quality-blocked (OcrBlocked), failed translations
/// (Failed), needing re-analysis (Stale) and waiting for a source subtitle
/// (AwaitingSource). Also owns the explicit POST re-check used by the tab.
/// </summary>
public class BlockedMediaService : IBlockedMediaService
{
    private static readonly TranslationState[] BlockedStates =
    [
        TranslationState.OcrBlocked,
        TranslationState.Failed,
        TranslationState.Stale,
        TranslationState.AwaitingSource
    ];

    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 500;
    private const int DefaultOcrMinimumQualityScore = 80;

    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settingService;
    private readonly IMediaStateService _mediaStateService;

    public BlockedMediaService(
        LingarrDbContext dbContext,
        ISettingService settingService,
        IMediaStateService mediaStateService)
    {
        _dbContext = dbContext;
        _settingService = settingService;
        _mediaStateService = mediaStateService;
    }

    /// <inheritdoc />
    public async Task<BlockedMediaPageResponse> GetBlockedMediaAsync(
        int page = 1,
        int pageSize = DefaultPageSize,
        TranslationState? state = null,
        CancellationToken cancellationToken = default)
    {
        var ocrMinimumQualityScore = await GetOcrMinimumQualityScoreAsync(cancellationToken);

        var movies = await _dbContext.Movies
            .AsNoTracking()
            .Include(m => m.EmbeddedSubtitles)
            .WhereNotMissing()
            .Where(m => BlockedStates.Contains(m.TranslationState))
            .ToListAsync(cancellationToken);

        var episodes = await _dbContext.Episodes
            .AsNoTracking()
            .Include(e => e.EmbeddedSubtitles)
            .WhereNotMissing()
            .Where(e => BlockedStates.Contains(e.TranslationState))
            .ToListAsync(cancellationToken);

        var items = new List<BlockedMediaItemResponse>(movies.Count + episodes.Count);

        items.AddRange(movies.Select(m => Map(
            "movie",
            m.Id,
            m.Title,
            m.TranslationState,
            m.EmbeddedSubtitles,
            m.LastSubtitleCheckAt,
            ocrMinimumQualityScore)));

        items.AddRange(episodes.Select(e => Map(
            "episode",
            e.Id,
            e.Title,
            e.TranslationState,
            e.EmbeddedSubtitles,
            e.LastSubtitleCheckAt,
            ocrMinimumQualityScore)));

        await PopulateFailureMessagesAsync(items, cancellationToken);

        var countsByState = items
            .GroupBy(item => item.TranslationState.ToString())
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var filteredItems = state.HasValue
            ? items.Where(item => item.TranslationState == state.Value)
            : items;

        var orderedItems = filteredItems
            .OrderBy(item => StateRank(item.TranslationState))
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.MediaType, StringComparer.Ordinal)
            .ThenBy(item => item.MediaId)
            .ToList();

        return BuildPage(orderedItems, countsByState, page, pageSize);
    }

    /// <inheritdoc />
    public async Task<BlockedMediaRecheckResponse?> RecheckMediaAsync(
        MediaType mediaType,
        int mediaId,
        CancellationToken cancellationToken = default)
    {
        if (mediaType is not (MediaType.Movie or MediaType.Episode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mediaType),
                mediaType,
                "Only movies and episodes can be re-checked.");
        }

        Movie? movie = null;
        Episode? episode = null;

        if (mediaType == MediaType.Movie)
        {
            movie = await _dbContext.Movies
                .Include(m => m.EmbeddedSubtitles)
                .FirstOrDefaultAsync(m => m.Id == mediaId, cancellationToken);
            if (movie == null)
            {
                return null;
            }
        }
        else
        {
            episode = await _dbContext.Episodes
                .Include(e => e.EmbeddedSubtitles)
                .FirstOrDefaultAsync(e => e.Id == mediaId, cancellationToken);
            if (episode == null)
            {
                return null;
            }
        }

        var evaluatedAt = DateTime.UtcNow;

        // An explicit re-check is a real evaluation, so it refreshes the "last checked"
        // timestamp the tab shows as freshness. Automatic sync still stamps only
        // AwaitingSource items (SubtitleCheckTimestampPolicy).
        if (movie != null)
        {
            movie.LastSubtitleCheckAt = evaluatedAt;
        }
        else if (episode != null)
        {
            episode.LastSubtitleCheckAt = evaluatedAt;
        }

        var state = await _mediaStateService.UpdateStateAsync(
            (IMedia?)movie ?? episode!,
            mediaType);

        return new BlockedMediaRecheckResponse
        {
            MediaId = mediaId,
            MediaType = mediaType == MediaType.Movie ? "movie" : "episode",
            State = state,
            EvaluatedAt = evaluatedAt
        };
    }

    private static BlockedMediaPageResponse BuildPage(
        List<BlockedMediaItemResponse> orderedItems,
        Dictionary<string, int> countsByState,
        int page,
        int pageSize)
    {
        var normalizedPageSize = pageSize <= 0
            ? DefaultPageSize
            : Math.Clamp(pageSize, 1, MaximumPageSize);
        var totalPages = Math.Max(
            1,
            (orderedItems.Count + normalizedPageSize - 1) / normalizedPageSize);
        var effectivePage = Math.Clamp(page <= 0 ? 1 : page, 1, totalPages);
        var skip = (effectivePage - 1) * normalizedPageSize;

        return new BlockedMediaPageResponse
        {
            Items = orderedItems.Skip(skip).Take(normalizedPageSize).ToList(),
            TotalCount = orderedItems.Count,
            PageNumber = effectivePage,
            PageSize = normalizedPageSize,
            CountsByState = countsByState
        };
    }

    private static BlockedMediaItemResponse Map(
        string mediaType,
        int mediaId,
        string title,
        TranslationState state,
        List<EmbeddedSubtitle> embeddedSubtitles,
        DateTime? lastSubtitleCheckAt,
        int ocrMinimumQualityScore)
    {
        var response = new BlockedMediaItemResponse
        {
            MediaId = mediaId,
            MediaType = mediaType,
            Title = title,
            TranslationState = state,
            LastSubtitleCheckAt = lastSubtitleCheckAt,
            LastEvaluatedAt = lastSubtitleCheckAt
        };

        if (state == TranslationState.OcrBlocked)
        {
            var blockedStream = FindBlockedStream(embeddedSubtitles);
            if (blockedStream != null)
            {
                response.StreamIndex = blockedStream.StreamIndex;
                response.OcrStatus = blockedStream.OcrStatus;
                response.OcrMinimumQualityScore = ocrMinimumQualityScore;
                response.LastEvaluatedAt = LatestOf(
                    blockedStream.OcrCompletedAt,
                    blockedStream.OcrAttemptedAt,
                    lastSubtitleCheckAt);

                if (blockedStream.OcrStatus == SubtitleOcrStatus.Failed)
                {
                    response.OcrQualityScore = null;
                    response.OcrIssueSummary = string.IsNullOrWhiteSpace(blockedStream.OcrError)
                        ? "OCR failed before producing usable subtitles."
                        : blockedStream.OcrError;
                }
                else
                {
                    response.OcrQualityScore = blockedStream.OcrQualityScore;
                    response.OcrPassesCurrentThreshold =
                        blockedStream.OcrStatus == SubtitleOcrStatus.BlockedLowQuality &&
                        blockedStream.OcrQualityScore.HasValue &&
                        blockedStream.OcrQualityScore.Value >= ocrMinimumQualityScore;
                    response.OcrIssueSummary = response.OcrPassesCurrentThreshold
                        ? RebuildPassingThresholdSummary(
                            blockedStream.OcrQualityScore!.Value,
                            ocrMinimumQualityScore)
                        : blockedStream.OcrIssueSummary;
                }
            }
        }

        return response;
    }

    /// <summary>
    /// Replaces issue text recorded against an older quality threshold when the
    /// stored score actually meets the threshold configured right now.
    /// </summary>
    private static string RebuildPassingThresholdSummary(int qualityScore, int minimumQualityScore)
    {
        return $"Quality score {qualityScore} meets the current minimum of {minimumQualityScore}. " +
               "A re-check promotes this OCR output without running OCR again.";
    }

    private async Task PopulateFailureMessagesAsync(
        List<BlockedMediaItemResponse> items,
        CancellationToken cancellationToken)
    {
        var failedItems = items
            .Where(item => item.TranslationState == TranslationState.Failed)
            .ToList();
        if (failedItems.Count == 0)
        {
            return;
        }

        var movieIds = failedItems
            .Where(item => string.Equals(item.MediaType, "movie", StringComparison.Ordinal))
            .Select(item => item.MediaId)
            .Distinct()
            .ToList();
        var episodeIds = failedItems
            .Where(item => string.Equals(item.MediaType, "episode", StringComparison.Ordinal))
            .Select(item => item.MediaId)
            .Distinct()
            .ToList();

        var failedRequests = await _dbContext.TranslationRequests
            .AsNoTracking()
            .Where(request => request.Status == TranslationStatus.Failed &&
                              request.MediaId.HasValue &&
                              ((request.MediaType == MediaType.Movie && movieIds.Contains(request.MediaId.Value)) ||
                               (request.MediaType == MediaType.Episode && episodeIds.Contains(request.MediaId.Value))))
            .Select(request => new
            {
                request.Id,
                request.MediaId,
                request.MediaType,
                request.FailedAt
            })
            .ToListAsync(cancellationToken);

        var latestRequestByMedia = failedRequests
            .GroupBy(request => MediaKey(request.MediaType, request.MediaId!.Value))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(request => request.FailedAt ?? DateTime.MinValue)
                    .ThenByDescending(request => request.Id)
                    .First());

        if (latestRequestByMedia.Count == 0)
        {
            return;
        }

        var requestIds = latestRequestByMedia.Values.Select(request => request.Id).ToList();
        var failureLogs = await _dbContext.TranslationRequestLogs
            .AsNoTracking()
            .Where(log => requestIds.Contains(log.TranslationRequestId) &&
                          (log.Level == "Error" || log.Level == "Warning"))
            .OrderByDescending(log => log.CreatedAt)
            .ThenByDescending(log => log.Id)
            .Select(log => new
            {
                log.TranslationRequestId,
                log.Message,
                log.Details
            })
            .ToListAsync(cancellationToken);

        var latestLogByRequestId = failureLogs
            .GroupBy(log => log.TranslationRequestId)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var item in failedItems)
        {
            if (!latestRequestByMedia.TryGetValue(
                    MediaKeyFromItem(item),
                    out var latestRequest))
            {
                continue;
            }

            item.LastEvaluatedAt = LatestOf(item.LastEvaluatedAt, latestRequest.FailedAt);
            item.FailureMessage = latestLogByRequestId.TryGetValue(latestRequest.Id, out var latestLog)
                ? string.IsNullOrWhiteSpace(latestLog.Details)
                    ? latestLog.Message
                    : latestLog.Details
                : "Translation request failed. See the translation logs for details.";
        }
    }

    private static (MediaType MediaType, int MediaId) MediaKey(MediaType mediaType, int mediaId)
    {
        return (mediaType, mediaId);
    }

    private static (MediaType MediaType, int MediaId) MediaKeyFromItem(BlockedMediaItemResponse item)
    {
        return (string.Equals(item.MediaType, "movie", StringComparison.Ordinal)
            ? MediaType.Movie
            : MediaType.Episode, item.MediaId);
    }

    private async Task<int> GetOcrMinimumQualityScoreAsync(CancellationToken cancellationToken)
    {
        var value = await _settingService.GetSetting(SettingKeys.SubtitleExtraction.OcrMinQualityScore);
        return int.TryParse(value, out var score)
            ? Math.Clamp(score, 0, 100)
            : DefaultOcrMinimumQualityScore;
    }

    private static DateTime? LatestOf(params DateTime?[] values)
    {
        return values.Length == 0
            ? null
            : values.Max();
    }

    private static int StateRank(TranslationState state) => state switch
    {
        TranslationState.OcrBlocked => 0,
        TranslationState.Failed => 1,
        TranslationState.Stale => 2,
        _ => 3
    };

    /// <summary>
    /// Picks the embedded subtitle stream responsible for the OCR block:
    /// the quality-blocked stream if present, otherwise a failed OCR stream,
    /// otherwise the first bitmap (image-based) stream.
    /// </summary>
    private static EmbeddedSubtitle? FindBlockedStream(List<EmbeddedSubtitle> embeddedSubtitles)
    {
        var byStreamIndex = embeddedSubtitles
            .OrderBy(subtitle => subtitle.StreamIndex)
            .ToList();

        return byStreamIndex.FirstOrDefault(subtitle =>
                   subtitle.OcrStatus == SubtitleOcrStatus.BlockedLowQuality)
               ?? byStreamIndex.FirstOrDefault(subtitle =>
                   subtitle.OcrStatus == SubtitleOcrStatus.Failed)
               ?? byStreamIndex.FirstOrDefault(subtitle => !subtitle.IsTextBased);
    }
}
