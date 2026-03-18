using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Models.Api;

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
    /// Lists all available subtitles for a media item with entry counts.
    /// </summary>
    /// <param name="mediaId">The media ID</param>
    /// <param name="mediaType">The type of media (Movie or Episode)</param>
    /// <returns>List of available subtitles with metadata and entry counts</returns>
    Task<List<AvailableSubtitleResponse>> ListAvailableSubtitlesAsync(int mediaId, MediaType mediaType);

    /// <summary>
    /// Extracts a specific subtitle stream to an external file.
    /// </summary>
    /// <param name="mediaFilePath">Path to the source media file</param>
    /// <param name="streamIndex">FFmpeg stream index of the subtitle track</param>
    /// <param name="outputDirectory">Directory to save the extracted subtitle</param>
    /// <param name="codecName">Codec name to determine output extension</param>
    /// <param name="language">Language code for the subtitle (e.g., "eng", "jpn"). Used in the output filename.</param>
    /// <returns>Path to the extracted file, or null if extraction failed</returns>
    Task<string?> ExtractSubtitle(string mediaFilePath, int streamIndex, string outputDirectory, string codecName, string? language);

    /// <summary>
    /// Syncs embedded subtitle information for an episode.
    /// Probes the media file and updates the database with detected embedded subtitles.
    /// </summary>
    /// <param name="episode">The episode to sync</param>
    Task SyncEmbeddedSubtitles(Episode episode);
    /// <summary>
    /// Tries to extract the best embedded subtitle for translation.
    /// Validates extracted subtitles have sufficient entries (skips sparse tracks like Signs/Songs).
    /// </summary>
    /// <param name="mediaId">The media ID</param>
    /// <param name="mediaType">The type of media (Movie or Episode)</param>
    /// <param name="sourceLanguage">The source language to extract</param>
    /// <param name="excludedStreamIndices">Stream indices to skip (already tried/failed)</param>
    /// <param name="preferredStreamIndex">Optional specific stream to extract first</param>
    /// <returns>Path to extracted subtitle, or null if no suitable subtitle found</returns>
    Task<string?> TryExtractEmbeddedSubtitle(
        int mediaId, 
        MediaType mediaType, 
        string sourceLanguage, 
        List<int>? excludedStreamIndices = null, 
        int? preferredStreamIndex = null);

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
