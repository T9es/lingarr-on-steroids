using Lingarr.Core.Enum;
using Lingarr.Server.Models.Subtitle;

namespace Lingarr.Server.Models.FileSystem;

public class TranslateAbleSubtitle
{
    public required int MediaId { get; set; }
    public TranslationWorkloadKind WorkloadKind { get; set; } = TranslationWorkloadKind.Library;
    public int? CustomMediaItemId { get; set; }
    public int? UploadBatchFileId { get; set; }
    /// <summary>
    /// Path to the source subtitle file. Can be empty for embedded subtitle extraction.
    /// </summary>
    public string? SubtitlePath { get; set; }
    public required string SourceLanguage { get; set; }
    public required string TargetLanguage { get; set; }
    public required MediaType MediaType { get; set; }
    /// <summary>
    /// Format of the subtitle file (e.g., ".srt", ".ass"). Can be empty for embedded subtitles.
    /// </summary>
    public string? SubtitleFormat { get; set; }
    public string? SourceSubtitleType { get; set; }
    public int SourceSubtitleEntryCount { get; set; }
    public string? SelectedStreamTitle { get; set; }
    public bool IsForcedSubtitle { get; set; }
    public SourceSubtitleSnapshot? SourceSnapshot { get; set; }
}
