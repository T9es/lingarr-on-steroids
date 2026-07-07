namespace Lingarr.Server.Models;

public class SubtitleQualityValidationRequest
{
    public required string SourcePath { get; set; }
    public required string TargetPath { get; set; }
    public string? SourceLanguage { get; set; }
    public string? TargetLanguage { get; set; }
    public string? OutputFormat { get; set; }
}
