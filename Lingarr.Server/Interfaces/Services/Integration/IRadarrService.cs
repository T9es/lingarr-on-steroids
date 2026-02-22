using Lingarr.Server.Models.Integrations;

namespace Lingarr.Server.Interfaces.Services.Integration;

/// <summary>
/// Defines a service for interacting with the Radarr API.
/// </summary>
public interface IRadarrService
{
    /// <summary>
    /// Asynchronously retrieves a list of movies from the Radarr API.
    /// </summary>
    /// <returns>
    /// This method calls the Radarr API to fetch movies
    /// The task result contains a <see cref="List{T}"/> of <see cref="RadarrMovie"/>
    /// objects representing all movies, or <c>null</c> if the API call fails.
    /// </returns>
    Task<List<RadarrMovie>?> GetMovies();

    /// <summary>
    /// Asynchronously retrieves a movie from the Radarr API.
    /// </summary>
    /// <returns>
    /// This method calls the Radarr API to fetch a movie
    /// The task result contains a <see cref="RadarrMovie"/>
    /// objects representing the movie, or <c>null</c> if the API call fails.
    /// </returns>
    Task<RadarrMovie?> GetMovie(int MovieId);

    /// <summary>
    /// Asynchronously retrieves a list of movies from a specific Radarr instance.
    /// Used for multi-instance support where URL/API key are provided directly.
    /// </summary>
    /// <param name="url">The Radarr server URL</param>
    /// <param name="apiKey">The Radarr API key</param>
    /// <returns>List of movies or null if the API call fails</returns>
    Task<List<RadarrMovie>?> GetMovies(string url, string apiKey);

    /// <summary>
    /// Asynchronously retrieves a movie from a specific Radarr instance.
    /// Used for multi-instance support where URL/API key are provided directly.
    /// </summary>
    /// <param name="movieId">The movie ID to retrieve</param>
    /// <param name="url">The Radarr server URL</param>
    /// <param name="apiKey">The Radarr API key</param>
    /// <returns>The movie or null if the API call fails</returns>
    Task<RadarrMovie?> GetMovie(int movieId, string url, string apiKey);
}