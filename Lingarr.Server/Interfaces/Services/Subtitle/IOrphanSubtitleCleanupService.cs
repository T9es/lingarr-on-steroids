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
}
