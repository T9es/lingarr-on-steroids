using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;

namespace Lingarr.Core.Entities;

public class Movie : BaseEntity, IMedia
{
    public required int RadarrId { get; set; }
    public required string Title { get; set; }
    public required string? FileName { get; set; }
    public required string? Path { get; set; }
    public string? MediaHash { get; set; } = string.Empty;
    public required DateTime? DateAdded { get; set; }
    public List<Image> Images { get; set; } = new();
    public bool ExcludeFromTranslation { get; set; }
    public int? TranslationAgeThreshold { get; set; }
    public bool IsPriority { get; set; }
    public DateTime? PriorityDate { get; set; }
    public List<EmbeddedSubtitle> EmbeddedSubtitles { get; set; } = new();
    
    /// <summary>
    /// Current translation state for efficient querying.
    /// Updated by MediaStateService when subtitles or settings change.
    /// </summary>
    public TranslationState TranslationState { get; set; } = TranslationState.Unknown;
    
    /// <summary>
    /// When embedded subtitles were last indexed via ffprobe.
    /// Null means never indexed - will be indexed during next sync.
    /// </summary>
    public DateTime? IndexedAt { get; set; }
    
    /// <summary>
    /// The language settings version when TranslationState was computed.
    /// If this doesn't match current version, state is stale.
    /// </summary>
    public int StateSettingsVersion { get; set; }
}