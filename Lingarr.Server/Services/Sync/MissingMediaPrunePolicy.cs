using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.Sync;

/// <summary>
/// Decides which media rows reported missing by Sonarr/Radarr are safe to hide and
/// eventually prune. Only rows without any translation history or per-item settings are
/// eligible; anything a user or a translation touched stays visible and is never pruned.
/// Embedded subtitle rows count as translation history, so a row that still carries them is
/// never hidden. Sync clears that stale stream metadata for media Sonarr/Radarr no longer
/// report before it evaluates eligibility, because indexed streams cannot belong to a file
/// that left the library.
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
    /// Parses the configured grace window. Blank or non-numeric values fall back to the
    /// default. Zero and negative values both disable pruning, so a mistyped negative number
    /// never starts a deletion run.
    /// </summary>
    public static int ResolvePruneWindowDays(string? settingValue)
    {
        if (string.IsNullOrWhiteSpace(settingValue))
        {
            return DefaultPruneWindowDays;
        }

        return int.TryParse(settingValue, out var days)
            ? Math.Max(days, 0)
            : DefaultPruneWindowDays;
    }

    /// <summary>
    /// Returns true when a missing movie has no translation history or per-item settings and
    /// may be hidden while missing and pruned after the grace window. Embedded subtitle rows
    /// count as history, so callers must clear that stale metadata first for movies Radarr
    /// stopped reporting entirely.
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
    /// Returns true when a missing episode has no translation history or per-item settings and
    /// may be hidden while missing and pruned after the grace window. Embedded subtitle rows
    /// count as history, so callers must clear that stale metadata first for episodes Sonarr
    /// stopped reporting entirely. The Season and Show navigations must be loaded by the
    /// caller.
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
