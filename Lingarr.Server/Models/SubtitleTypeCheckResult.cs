namespace Lingarr.Server.Models;

/// <summary>
/// Result of subtitle type validation check.
/// Detects potentially incomplete subtitles (Forced/Signs-only) based on entry count.
/// </summary>
public class SubtitleTypeCheckResult
{
    /// <summary>
    /// The translation request ID that was checked.
    /// </summary>
    public int TranslationId { get; set; }

    /// <summary>
    /// Title of the media being checked.
    /// </summary>
    public string MediaTitle { get; set; } = string.Empty;

    /// <summary>
    /// Path to the source subtitle file that was analyzed.
    /// </summary>
    public string SubtitlePath { get; set; } = string.Empty;

    /// <summary>
    /// Number of subtitle entries (dialogue lines) found in the file.
    /// </summary>
    public int EntryCount { get; set; }

    /// <summary>
    /// Indicates if the subtitle is considered complete (>= 50 entries).
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// Warning message if subtitle appears incomplete.
    /// Example: "Only 12 entries - likely Forced subtitle"
    /// </summary>
    public string Warning { get; set; } = string.Empty;

    /// <summary>
    /// Recommended action to take for incomplete subtitles.
    /// Example: "Re-translate with different subtitle"
    /// </summary>
    public string RecommendedAction { get; set; } = string.Empty;

    /// <summary>
    /// Media type: "Movie" or "Episode"
    /// </summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>
    /// Media ID for requeue operations.
    /// </summary>
    public int MediaId { get; set; }

    /// <summary>
    /// Indicates whether this media item is already queued for translation.
    /// </summary>
    public bool IsQueued { get; set; }

    /// <summary>
    /// Whether the user has dismissed this warning.
    /// </summary>
    public bool Dismissed { get; set; }
}

/// <summary>
/// Aggregated result containing all subtitle type issues found during bulk check.
/// </summary>
public class SubtitleTypeCheckSummary
{
    /// <summary>
    /// Total number of translations scanned.
    /// </summary>
    public int TotalScanned { get; set; }

    /// <summary>
    /// Number of translations with potentially incomplete subtitles.
    /// </summary>
    public int IncompleteCount { get; set; }

    /// <summary>
    /// List of flagged incomplete subtitle results.
    /// </summary>
    public List<SubtitleTypeCheckResult> FlaggedItems { get; set; } = new();
}
