namespace Lingarr.Server.Models.Api;

public class CompletedTranslationCompareResponse
{
    public int TranslationRequestId { get; set; }
    public required string Title { get; set; }
    public required string SourceLanguage { get; set; }
    public required string TargetLanguage { get; set; }
    public required string MediaType { get; set; }
    public DateTime? CompletedAt { get; set; }
    public required string OriginalSubtitlePath { get; set; }
    public required string TranslatedSubtitlePath { get; set; }
    public required string SourceFingerprint { get; set; }
    public int OriginalLineCount { get; set; }
    public int TranslatedLineCount { get; set; }
    public List<TranslationCompareLineDto> Lines { get; set; } = [];
    public bool IsPartialFailure { get; set; }
    public List<int> MissingPositions { get; set; } = [];
    public bool CanAccept { get; set; }
}
