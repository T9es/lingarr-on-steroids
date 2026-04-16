namespace Lingarr.Server.Models.Api;

public class TranslationCompareLineDto
{
    public int Position { get; set; }
    public string Original { get; set; } = string.Empty;
    public string? Translated { get; set; }
    public bool Success { get; set; }
    public int? DurationMs { get; set; }
    public int? StartTimeMs { get; set; }
    public int? EndTimeMs { get; set; }
}
