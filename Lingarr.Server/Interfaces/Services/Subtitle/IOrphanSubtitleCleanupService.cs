namespace Lingarr.Server.Interfaces.Services.Subtitle;

/// <summary>
/// Service for cleaning up orphaned subtitle files when media files are upgraded.
/// </summary>
public interface IOrphanSubtitleCleanupService
{
    /// <summary>
    /// Cleans up orphaned subtitles when a media file's name changes.
    /// Only removes subtitle files that contain the configured Lingarr tag.
    /// </summary>
    /// <param name="directoryPath">Directory containing the media</param>
    /// <param name="oldFileName">Previous media filename (without extension)</param>
    /// <param name="newFileName">New media filename (without extension)</param>
    /// <returns>Number of files cleaned up</returns>
    Task<int> CleanupOrphansAsync(string directoryPath, string oldFileName, string newFileName);

    /// <summary>
    /// Cleans up stale translated subtitles for a specific media file and target language.
    /// </summary>
    /// <param name="directoryPath">Directory containing the media</param>
    /// <param name="fileName">Media filename (without extension)</param>
    /// <param name="targetLanguage">Target language code (optional, if null cleans all translated subs for this media)</param>
    /// <returns>Number of files cleaned up</returns>
    Task<int> CleanupStaleSubtitlesAsync(string directoryPath, string fileName, string? targetLanguage = null);
}
