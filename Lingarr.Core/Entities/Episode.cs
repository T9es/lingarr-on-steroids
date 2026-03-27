using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;

namespace Lingarr.Core.Entities;

public class Episode : BaseEntity, IMedia
{
    public required int SonarrId { get; set; }
    
    /// <summary>
    /// Identifier for the Sonarr instance this episode was synced from.
    /// Propagated from parent Show during sync for efficient querying.
    /// </summary>
    [MaxLength(100)]
    public string? SourceInstanceId { get; set; }
    
    public required int EpisodeNumber { get; set; }
    public required string Title { get; set; }
    public string? FileName { get; set; } = string.Empty;
    public string? Path { get; set; } = string.Empty;
    public string? MediaHash { get; set; } = string.Empty;
    public DateTime? DateAdded { get; set; }

    public int SeasonId { get; set; }
    [ForeignKey(nameof(SeasonId))]
    public required Season Season { get; set; }
    public bool ExcludeFromTranslation { get; set; }
    public List<EmbeddedSubtitle> EmbeddedSubtitles { get; set; } = new();
    
    /// <summary>
    /// Current translation state for efficient querying.
    /// </summary>
    [JsonConverter(typeof(JsonNumberEnumConverter<TranslationState>))]
    public TranslationState TranslationState { get; set; } = TranslationState.Unknown;
    
    /// <summary>
    /// When embedded subtitles were last indexed via ffprobe.
    /// </summary>
    public DateTime? IndexedAt { get; set; }
    
    /// <summary>
    /// The language settings version when TranslationState was computed.
    /// </summary>
    public int StateSettingsVersion { get; set; }
    
    /// <summary>
    /// When the media directory was last checked while waiting for a source subtitle,
    /// or when automation rotated this item to the back of the queue.
    /// </summary>
    public DateTime? LastSubtitleCheckAt { get; set; }
}
