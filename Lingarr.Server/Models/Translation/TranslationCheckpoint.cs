namespace Lingarr.Server.Models.Translation;

public class TranslationCheckpoint
{
    public int TranslationRequestId { get; set; }
    public string SourceFingerprint { get; set; } = string.Empty;
    public Dictionary<int, string> Translations { get; set; } = new();
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
