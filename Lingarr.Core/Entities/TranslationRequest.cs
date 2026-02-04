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
}
