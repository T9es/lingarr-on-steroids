using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;

namespace Lingarr.Core.Entities;

public class CustomMediaItem : BaseEntity, IMedia
{
    public required int CustomSourceId { get; set; }
    public required CustomSource CustomSource { get; set; }
    public required CustomMediaItemKind ItemKind { get; set; }
    public required string Title { get; set; }
    public required string FileName { get; set; }
    public required string Path { get; set; }
    public required string RelativePath { get; set; }
    public string? MediaHash { get; set; } = string.Empty;
    public DateTime? DateAdded { get; set; }
    public TranslationState TranslationState { get; set; } = TranslationState.Unknown;
    public DateTime? IndexedAt { get; set; }
    public int StateSettingsVersion { get; set; }
    public DateTime? LastSubtitleCheckAt { get; set; }
    public bool ExcludeFromTranslation { get; set; }
    public bool IsPriority { get; set; }
    public DateTime? PriorityDate { get; set; }
    public string? SeriesTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
}
