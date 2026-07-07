namespace Lingarr.Server.Models.Translation;

public sealed class TranslationPromptContext
{
    public bool IsOcrDerivedSource { get; set; }
    public string? MovieTitle { get; set; }
    public string? SeriesTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public string? EpisodeTitle { get; set; }
    public string? SourceLanguage { get; set; }
    public string? TargetLanguage { get; set; }
    public string? SelectedStreamTitle { get; set; }
    public string? SourceSubtitleType { get; set; }
    public string? SourceNote { get; set; }
}
