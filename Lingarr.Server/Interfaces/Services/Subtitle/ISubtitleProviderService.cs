using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;

namespace Lingarr.Server.Interfaces.Services.Subtitle;

/// <summary>
/// Service for downloading subtitles from external providers like OpenSubtitles and Subdl.
/// </summary>
public interface ISubtitleProviderService
{
    /// <summary>
    /// Searches for and downloads a subtitle for the given media.
    /// Uses a tiered search strategy: IMDB ID -> TMDB ID -> Title query.
    /// </summary>
    /// <param name="media">The media item to find subtitles for</param>
    /// <param name="mediaType">Type of media (Movie or Episode)</param>
    /// <param name="language">Target language code (e.g., "en", "nl")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Path to downloaded subtitle file, or null if not found</returns>
    Task<string?> SearchAndDownloadSubtitle(IMedia media, MediaType mediaType, string language, CancellationToken cancellationToken);
}
