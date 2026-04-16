namespace Lingarr.Server.Models.Subtitle;

public class SourceSubtitleSnapshot
{
    public const int CurrentVersion = 1;
    public const string ExternalType = "external";
    public const string EmbeddedType = "embedded";

    public int Version { get; init; } = CurrentVersion;
    public string SourceType { get; init; } = string.Empty;
    public string SourceLanguage { get; init; } = string.Empty;
    public string Identity { get; init; } = string.Empty;
    public string Fingerprint { get; init; } = string.Empty;
    public string? SourcePath { get; init; }
    public long? FileSizeBytes { get; init; }
    public DateTime? LastWriteUtc { get; init; }
    public int? StreamIndex { get; init; }
}
