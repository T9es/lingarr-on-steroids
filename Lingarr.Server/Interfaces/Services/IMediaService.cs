using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Models.Sync;

namespace Lingarr.Server.Interfaces.Services;

/// <summary>
/// Defines a service for retrieving and managing media information.
/// </summary>
public interface IMediaService
{
    /// <summary>
    /// Retrieves a paginated and optionally filtered and ordered list of movies asynchronously.
    /// </summary>
    /// <param name="searchQuery">An optional search query to filter movies by title. If null or empty, no filtering is applied.</param>
    /// <param name="orderBy">An optional field to order the results by. Possible values include "Id", "Title", and "DateAdded".</param>
    /// <param name="ascending">Specifies whether the sorting should be in ascending order. If false, results are sorted in descending order.</param>
    /// <param name="pageNumber">The number of the page to retrieve. Must be greater than or equal to 1.</param>
    /// <param name="pageSize">The number of items per page. Must be greater than or equal to 1.</param>
    /// <returns>
    /// A task result containing a <see cref="PagedResult{MovieResponse}"/> a list of movies, 
    /// along with the total count of movies that match the criteria.
    /// </returns>
    Task<PagedResult<MovieResponse>> GetMovies(
        string? searchQuery,
        string? orderBy,
        bool ascending,
        int pageNumber,
        int pageSize);

    /// <summary>
    /// Retrieves a paginated and optionally filtered and ordered list of shows asynchronously.
    /// </summary>
    /// <param name="searchQuery">An optional search query to filter shows by title. If null or empty, no filtering is applied.</param>
    /// <param name="orderBy">An optional field to order the results by. Possible values include "Id", "Title", and "DateAdded".</param>
    /// <param name="ascending">Specifies whether the sorting should be in ascending order. If false, results are sorted in descending order.</param>
    /// <param name="pageNumber">The number of the page to retrieve. Must be greater than or equal to 1.</param>
    /// <param name="pageSize">The number of items per page. Must be greater than or equal to 1.</param>
    /// <returns>
    /// A task result containing a <see cref="PagedResult{Show}"/> a list of shows, 
    /// along with the total count of shows that match the criteria.
    /// </returns>
    Task<PagedResult<Show>> GetShows(
        string? searchQuery,
        string? orderBy,
        bool ascending,
        int pageNumber,
        int pageSize);

    /// <summary>
    /// Retrieves a specific show by its ID, including its seasons and episodes.
    /// </summary>
    /// <param name="id">The unique identifier of the show.</param>
    /// <returns>
    /// A task result containing the show if found, or null if not found.
    /// </returns>
    Task<Show?> GetShow(int id);

    /// <summary>
    /// Retrieves a movie id (lingarr's id) from the database with a Radarr movie id.
    /// If it is not in the database, it will try to sync the Movie with Radarr
    /// If the Movie is not found in Rdarr either, 0 will be returned
    /// </summary>
    /// <param name="movieId">The Radarr movie id to search with</param>
    /// <param name="sourceInstanceId">Optional source instance ID to filter by in multi-instance setups</param>
    /// <returns>
    /// A task result containing the lingarr's movie id
    /// </returns>
    Task<int> GetMovieIdOrSyncFromRadarrMovieId(int movieId, string? sourceInstanceId = null);

    /// <summary>
    /// Retrieves an episode id (lingarr's id) from the database with a Sonarr episode id.
    /// If it is not in the database, it will try to sync the Show with Sonarr
    /// If the Show is not found in Sonarr either, 0 will be returned
    /// </summary>
    /// <param name="episodeNumber">The Sonarr episode id to search with</param>
    /// <param name="sourceInstanceId">Optional source instance ID to filter by in multi-instance setups</param>
    /// <returns>
    /// A task result containing the lingarr's episode id
    /// </returns>
    Task<int> GetEpisodeIdOrSyncFromSonarrEpisodeId(int episodeNumber, string? sourceInstanceId = null);

    /// <summary>
    /// Refreshes a single episode from Sonarr for webhook processing.
    /// Always fetches fresh upstream state instead of reusing cached local metadata.
    /// Returns null when the episode does not exist, has no file, or cannot be refreshed.
    /// </summary>
    /// <param name="sonarrEpisodeId">The Sonarr episode id to refresh</param>
    /// <param name="sourceInstanceId">Optional source instance ID to filter by in multi-instance setups</param>
    /// <returns>The targeted refresh result or null if refresh could not complete</returns>
    Task<EpisodeRefreshResult?> RefreshEpisodeFromSonarrEpisodeId(int sonarrEpisodeId, string? sourceInstanceId = null);

    /// <summary>
    /// Refreshes a single movie from Radarr for webhook processing.
    /// Always fetches fresh upstream state instead of reusing cached local metadata.
    /// Returns null when the movie does not exist, has no file, or cannot be refreshed.
    /// </summary>
    /// <param name="radarrMovieId">The Radarr movie id to refresh</param>
    /// <param name="sourceInstanceId">Optional source instance ID to filter by in multi-instance setups</param>
    /// <returns>The targeted refresh result or null if refresh could not complete</returns>
    Task<MovieRefreshResult?> RefreshMovieFromRadarrMovieId(int radarrMovieId, string? sourceInstanceId = null);

    /// <summary>
    /// Toggles the exclusion status of a media item from translation.
    /// </summary>
    /// <param name="mediaType">The type of media (Movie, Show, Season, or Episode).</param>
    /// <param name="id">The unique identifier of the media item.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains a boolean value:
    /// - true if the exclusion status was successfully toggled
    /// - false if the item was not found or an error occurred
    /// </returns>
    Task<bool> Exclude(MediaType mediaType, int id);

    /// <summary>
    /// Sets the amount of hours a media file needs to exist before translation is initiated.
    /// </summary>
    /// <param name="mediaType">The type of media (Movie, Show, Season, or Episode).</param>
    /// <param name="id">The unique identifier of the media item.</param>
    /// <param name="hours">The amount of hours that needs to be set</param>
    Task<bool> Threshold(MediaType mediaType, int id, int hours);
    
    /// <summary>
    /// Toggles the priority status of a media item.
    /// </summary>
    /// <param name="mediaType">The type of media (Movie, Show, Season, or Episode).</param>
    /// <param name="id">The unique identifier of the media item.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains a boolean value:
    /// - true if the priority status was successfully toggled
    /// - false if the item was not found or an error occurred
    /// </returns>
    Task<bool> TogglePriority(MediaType mediaType, int id);
    /// <summary>
    /// Performs an integrity check on the subtitles of a media item without re-translating if valid.
    /// </summary>
    /// <param name="mediaType">The type of media (Movie, Show, Season, or Episode).</param>
    /// <param name="id">The unique identifier of the media item.</param>
    /// <returns>
    /// A task result containing an integer indicating the number of repair translations queued.
    /// </returns>
    Task<int> IntegrityCheck(MediaType mediaType, int id);
    
    /// <summary>
    /// Starts a bulk integrity check for all Complete-state media items.
    /// </summary>
    /// <returns>The Hangfire job ID for tracking progress.</returns>
    string StartBulkIntegrityCheck();
}
