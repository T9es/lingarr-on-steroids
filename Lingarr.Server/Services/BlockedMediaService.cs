using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.Api;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

/// <summary>
/// Queries media items that are blocked from translation so they can be
/// surfaced in the UI: OCR quality-blocked (OcrBlocked), needing re-analysis
/// (Stale) and waiting for a source subtitle (AwaitingSource).
/// </summary>
public class BlockedMediaService : IBlockedMediaService
{
    private readonly LingarrDbContext _dbContext;

    public BlockedMediaService(LingarrDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<List<BlockedMediaItemResponse>> GetBlockedMediaAsync(
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var states = new[]
        {
            TranslationState.OcrBlocked,
            TranslationState.Stale,
            TranslationState.AwaitingSource
        };

        var movies = await _dbContext.Movies
            .AsNoTracking()
            .Include(m => m.EmbeddedSubtitles)
            .Where(m => states.Contains(m.TranslationState))
            .ToListAsync(cancellationToken);

        var episodes = await _dbContext.Episodes
            .AsNoTracking()
            .Include(e => e.EmbeddedSubtitles)
            .Where(e => states.Contains(e.TranslationState))
            .ToListAsync(cancellationToken);

        var items = new List<BlockedMediaItemResponse>(movies.Count + episodes.Count);

        items.AddRange(movies.Select(m => Map(
            "movie",
            m.Id,
            m.Title,
            m.TranslationState,
            m.EmbeddedSubtitles,
            m.LastSubtitleCheckAt)));

        items.AddRange(episodes.Select(e => Map(
            "episode",
            e.Id,
            e.Title,
            e.TranslationState,
            e.EmbeddedSubtitles,
            e.LastSubtitleCheckAt)));

        return items
            .OrderBy(item => StateRank(item.TranslationState))
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, limit))
            .ToList();
    }

    private static int StateRank(TranslationState state) => state switch
    {
        TranslationState.OcrBlocked => 0,
        TranslationState.Stale => 1,
        _ => 2
    };

    private static BlockedMediaItemResponse Map(
        string mediaType,
        int mediaId,
        string title,
        TranslationState state,
        List<EmbeddedSubtitle> embeddedSubtitles,
        DateTime? lastSubtitleCheckAt)
    {
        var response = new BlockedMediaItemResponse
        {
            MediaId = mediaId,
            MediaType = mediaType,
            Title = title,
            TranslationState = state,
            LastSubtitleCheckAt = lastSubtitleCheckAt
        };

        if (state == TranslationState.OcrBlocked)
        {
            var blockedStream = FindBlockedStream(embeddedSubtitles);
            if (blockedStream != null)
            {
                response.StreamIndex = blockedStream.StreamIndex;
                response.OcrStatus = blockedStream.OcrStatus;
                response.OcrQualityScore = blockedStream.OcrQualityScore;
                response.OcrIssueSummary = blockedStream.OcrIssueSummary;
            }
        }

        return response;
    }

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
