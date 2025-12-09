using Lingarr.Core.Entities;

namespace Lingarr.Server.Interfaces.Services.Subtitle;

/// <summary>
/// Service for detecting and extracting embedded subtitles from media files using FFmpeg.
/// </summary>
public interface ISubtitleExtractionService
{
    /// <summary>
    /// Probes a media file and returns information about all embedded subtitle streams.
    /// </summary>
    /// <param name="mediaFilePath">Path to the media file (MKV, MP4, etc.)</param>
    /// <returns>List of detected embedded subtitle streams</returns>
    Task<List<EmbeddedSubtitle>> ProbeEmbeddedSubtitles(string mediaFilePath);

    /// <summary>
    /// Extracts a specific subtitle stream to an external file.
    /// </summary>
    /// <param name="mediaFilePath">Path to the source media file</param>
    /// <param name="streamIndex">FFmpeg stream index of the subtitle track</param>
    /// <param name="outputDirectory">Directory to save the extracted subtitle</param>
    /// <param name="codecName">Codec name to determine output extension</param>
    /// <returns>Path to the extracted file, or null if extraction failed</returns>
    Task<string?> ExtractSubtitle(string mediaFilePath, int streamIndex, string outputDirectory, string codecName);

    /// <summary>
    /// Syncs embedded subtitle information for an episode.
    /// Probes the media file and updates the database with detected embedded subtitles.
    /// </summary>
    /// <param name="episode">The episode to sync</param>
    Task SyncEmbeddedSubtitles(Episode episode);

    /// <summary>
    /// Syncs embedded subtitle information for a movie.
    /// Probes the media file and updates the database with detected embedded subtitles.
    /// </summary>
    /// <param name="movie">The movie to sync</param>
    Task SyncEmbeddedSubtitles(Movie movie);

    /// <summary>
    /// Checks if FFmpeg is available on the system.
    /// </summary>
    /// <returns>True if FFmpeg is installed and accessible</returns>
    Task<bool> IsFfmpegAvailable();
}
