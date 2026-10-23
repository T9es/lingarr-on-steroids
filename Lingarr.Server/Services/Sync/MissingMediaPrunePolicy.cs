using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.Sync;

/// <summary>
/// Decides which media rows reported missing by Sonarr/Radarr are safe to hide and
/// eventually prune. Only rows without any translation history or per-item settings are
/// eligible; anything a user or a translation touched stays visible and is never pruned.
/// </summary>
public static class MissingMediaPrunePolicy
{
    /// <summary>
    /// Grace window used when the prune_missing_media_days setting is absent or invalid.
    /// </summary>
    public const int DefaultPruneWindowDays = 7;

    /// <summary>
    /// Upper bound on rows inspected and deleted by a single cleanup run.
    /// </summary>
    public const int MaxPruneBatchSize = 250;

    /// <summary>
    /// Parses the configured grace window. Blank or invalid values fall back to the default,
    /// and 0 or a negative value disables pruning.
    /// </summary>
    public static int ResolvePruneWindowDays(string? settingValue)
    {
        if (string.IsNullOrWhiteSpace(settingValue))
        {
            return DefaultPruneWindowDays;
        }

        return int.TryParse(settingValue, out var days) && days >= 0
            ? days
            : DefaultPruneWindowDays;
    }

    /// <summary>
    /// Returns true when a missing movie has no history or per-item settings and may be
    /// hidden while missing and pruned after the grace window.
    /// </summary>
    public static async Task<bool> IsEligibleForMissingTrackingAsync(
        LingarrDbContext dbContext,
        Movie movie,
        CancellationToken cancellationToken = default)
    {
        if (!HasNoPerItemSettings(movie))
        {
            return false;
        }

        return await HasNoTranslationHistoryAsync(
            dbContext,
            movie.Id,
            MediaType.Movie,
            cancellationToken);
    }

    /// <summary>
    /// Returns true when a missing episode has no history or per-item settings and may be
    /// hidden while missing and pruned after the grace window. The Season and Show
    /// navigations must be loaded by the caller.
    /// </summary>
    public static async Task<bool> IsEligibleForMissingTrackingAsync(
        LingarrDbContext dbContext,
        Episode episode,
        CancellationToken cancellationToken = default)
    {
        if (!HasNoPerItemSettings(episode))
        {
            return false;
        }

        return await HasNoTranslationHistoryAsync(
            dbContext,
            episode.Id,
            MediaType.Episode,
            cancellationToken);
    }

    private static bool HasNoPerItemSettings(Movie movie)
    {
        return !movie.ExcludeFromTranslation
               && !movie.IsPriority
               && movie.PriorityDate == null
               && movie.TranslationAgeThreshold == null;
    }

    private static bool HasNoPerItemSettings(Episode episode)
    {
        if (episode.ExcludeFromTranslation)
        {
            return false;
        }

        if (episode.Season == null || episode.Season.ExcludeFromTranslation)
        {
            return false;
        }

        var show = episode.Season.Show;
        return show != null
               && !show.ExcludeFromTranslation
               && !show.IsPriority
               && show.PriorityDate == null
               && show.TranslationAgeThreshold == null;
    }

    private static async Task<bool> HasNoTranslationHistoryAsync(
        LingarrDbContext dbContext,
        int mediaId,
        MediaType mediaType,
        CancellationToken cancellationToken)
    {
        var hasRequest = await dbContext.TranslationRequests
            .AsNoTracking()
            .AnyAsync(
                request => request.MediaId == mediaId && request.MediaType == mediaType,
                cancellationToken);
        if (hasRequest)
        {
            return false;
        }

        var hasDiagnostic = await dbContext.TranslationDiagnosticEvents
            .AsNoTracking()
            .AnyAsync(
                diagnostic => diagnostic.MediaId == mediaId && diagnostic.MediaType == mediaType,
                cancellationToken);
        if (hasDiagnostic)
        {
            return false;
        }

        var hasEmbeddedSubtitles = mediaType == MediaType.Movie
            ? await dbContext.EmbeddedSubtitles
                .AsNoTracking()
                .AnyAsync(subtitle => subtitle.MovieId == mediaId, cancellationToken)
            : await dbContext.EmbeddedSubtitles
                .AsNoTracking()
                .AnyAsync(subtitle => subtitle.EpisodeId == mediaId, cancellationToken);

        return !hasEmbeddedSubtitles;
    }
}
