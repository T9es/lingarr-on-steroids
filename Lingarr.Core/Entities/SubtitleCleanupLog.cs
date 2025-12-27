namespace Lingarr.Core.Entities;

/// <summary>
/// Tracks deletions of orphaned subtitle files for audit purposes.
/// </summary>
public class SubtitleCleanupLog
{
    public int Id { get; set; }
    
    /// <summary>
    /// Full path of the deleted subtitle file.
    /// </summary>
    public required string FilePath { get; set; }
    
    /// <summary>
    /// The original media filename that this subtitle was associated with.
    /// </summary>
    public required string OriginalMediaFileName { get; set; }
    
    /// <summary>
    /// The new media filename that replaced the original.
    /// </summary>
    public required string NewMediaFileName { get; set; }
    
    /// <summary>
    /// Reason for cleanup (e.g., "media_filename_changed").
    /// </summary>
    public required string Reason { get; set; }
    
    /// <summary>
    /// Timestamp when the file was deleted.
    /// </summary>
    public DateTime DeletedAt { get; set; } = DateTime.UtcNow;
}
