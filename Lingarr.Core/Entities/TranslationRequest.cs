using Lingarr.Core.Enum;

namespace Lingarr.Core.Entities;

public class TranslationRequest : BaseEntity
{
    public string? JobId  { get; set; }
    public int? MediaId  { get; set; }
    public required string Title { get; set; }
    public required string SourceLanguage { get; set; }
    public required string TargetLanguage { get; set; }
    public string? SubtitleToTranslate { get; set; }
    public string? TranslatedSubtitle { get; set; }
    public required MediaType MediaType { get; set; }
    public required TranslationStatus Status { get; set; }
    public bool? IsActive { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int Progress { get; set; }
    
    /// <summary>
    /// Persisted priority flag for queue ordering.
    /// Set from Media's IsPriority when request is created.
    /// Updated when Media priority changes via MediaService.TogglePriority().
    /// </summary>
    public bool IsPriority { get; set; }
    
    // Subtitle tracking fields for audit/debugging purposes
    /// <summary>
    /// The type of source subtitle used for translation (e.g., "Full", "SDH", "Forced", "Unknown")
    /// </summary>
    public string? SourceSubtitleType { get; set; }
    
    /// <summary>
    /// The number of subtitle entries in the source file
    /// </summary>
    public int SourceSubtitleEntryCount { get; set; }
    
    /// <summary>
    /// The original stream title from the video file metadata
    /// </summary>
    public string? SelectedStreamTitle { get; set; }
    
    /// <summary>
    /// Indicates if a forced subtitle stream was used for translation
    /// </summary>
    public bool IsForcedSubtitle { get; set; }
    
    /// <summary>
    /// When the translation actually started processing (not just queued)
    /// </summary>
    public DateTime? StartedAt { get; set; }
    
    /// <summary>
    /// Number of times this request has been retried after failure
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Version of the source snapshot format used to capture source freshness metadata.
    /// </summary>
    public int SourceSnapshotVersion { get; set; } = 1;

    /// <summary>
    /// Source snapshot type used by this translation request ("external" or "embedded").
    /// </summary>
    public string? SourceSnapshotType { get; set; }

    /// <summary>
    /// Stable identity for the source subtitle candidate used during translation.
    /// </summary>
    public string? SourceSnapshotIdentity { get; set; }

    /// <summary>
    /// Fingerprint of the source subtitle revision used for translation.
    /// </summary>
    public string? SourceSnapshotFingerprint { get; set; }

    /// <summary>
    /// Source subtitle file size in bytes when the request was translated (external source only).
    /// </summary>
    public long? SourceSnapshotFileSizeBytes { get; set; }

    /// <summary>
    /// Source subtitle file last write UTC timestamp when translated (external source only).
    /// </summary>
    public DateTime? SourceSnapshotLastWriteUtc { get; set; }

    /// <summary>
    /// Selected source stream index when translating from embedded subtitles.
    /// </summary>
    public int? SourceSnapshotStreamIndex { get; set; }
    
    /// <summary>
    /// When the request last failed (null if never failed)
    /// </summary>
    public DateTime? FailedAt { get; set; }
    
    /// <summary>
    /// When the request should be retried next (null if not scheduled for retry)
    /// Used for exponential backoff of failed requests
    /// </summary>
    public DateTime? NextRetryAt { get; set; }
}
