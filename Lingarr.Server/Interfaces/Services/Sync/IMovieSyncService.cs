using Lingarr.Core.Entities;
using Lingarr.Server.Models.Integrations;

namespace Lingarr.Server.Interfaces.Services.Sync;

public interface IMovieSyncService
{
    /// <summary>
    /// Synchronizes multiple movies from Radarr with their instance IDs
    /// </summary>
    /// <param name="movies">The list of movies with their instance IDs to sync</param>
    /// <returns>A task representing the asynchronous operation</returns>
    Task SyncMovies(List<(RadarrMovie Movie, string InstanceId)> movies);

    /// <summary>
    /// Synchronizes a movie from Radarr
    /// </summary>
    /// <param name="movie">The Radarr movie to sync</param>
    /// <param name="instanceId">The ID of the Radarr instance this movie belongs to</param>
    /// <returns>A task representing the asynchronous operation</returns>
    Task<Movie?> SyncMovie(RadarrMovie movie, string instanceId);

    /// <summary>
    /// Removes movies that no longer exist in Radarr for a specific instance
    /// </summary>
    /// <param name="existingRadarrIds">The collection of currently existing Radarr movie IDs</param>
    /// <param name="instanceId">The ID of the Radarr instance to filter by</param>
    /// <returns>A task representing the asynchronous operation</returns>
    Task RemoveNonExistentMovies(IEnumerable<int> existingRadarrIds, string instanceId);
}
