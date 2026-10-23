using Lingarr.Core.Entities;

namespace Lingarr.Server.Services.Media;

/// <summary>
/// Visibility helpers for media that Sonarr/Radarr reported as missing. Such rows stay in
/// the database during the prune grace window but must not appear in blocked media listings,
/// automation or other work queues.
/// </summary>
public static class MissingMediaQueryExtensions
{
    /// <summary>
    /// Excludes movies that Sonarr/Radarr reported missing and that are waiting for the
    /// prune grace window to elapse.
    /// </summary>
    public static IQueryable<Movie> WhereNotMissing(this IQueryable<Movie> query)
    {
        return query.Where(movie => movie.MissingSince == null);
    }

    /// <summary>
    /// Excludes episodes that Sonarr/Radarr reported missing and that are waiting for the
    /// prune grace window to elapse.
    /// </summary>
    public static IQueryable<Episode> WhereNotMissing(this IQueryable<Episode> query)
    {
        return query.Where(episode => episode.MissingSince == null);
    }

    /// <summary>
    /// Returns true when Sonarr/Radarr reported this movie as missing.
    /// </summary>
    public static bool IsMissing(this Movie movie)
    {
        return movie.MissingSince.HasValue;
    }

    /// <summary>
    /// Returns true when Sonarr/Radarr reported this episode as missing.
    /// </summary>
    public static bool IsMissing(this Episode episode)
    {
        return episode.MissingSince.HasValue;
    }
}
