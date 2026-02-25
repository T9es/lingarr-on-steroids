using System.ComponentModel.DataAnnotations;

namespace Lingarr.Core.Entities;

/// <summary>
/// Tracks errors that occur during translation and sync operations.
/// Provides visibility into issues via the dashboard.
/// </summary>
public class ErrorLog
{
    [Key]
    public int Id { get; set; }
    
    /// <summary>
    /// When the error occurred (UTC)
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// The source of the error (e.g., "TranslationJob", "SyncMovieJob", "DashboardService")
    /// </summary>
    [MaxLength(100)]
    public string Source { get; set; } = string.Empty;
    
    /// <summary>
    /// Brief error message
    /// </summary>
    [MaxLength(500)]
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Additional context or details
    /// </summary>
    [MaxLength(2000)]
    public string? Details { get; set; }
    
    /// <summary>
    /// Stack trace for debugging
    /// </summary>
    public string? StackTrace { get; set; }
}
