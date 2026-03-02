namespace Lingarr.Core.Entities;

public class TestResult : BaseEntity
{
    public required string SubtitlePath { get; set; }
    public string? Title { get; set; }
    public string? PosterPath { get; set; }
    public required string SourceLanguage { get; set; }
    public required string TargetLanguage { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int TotalLines { get; set; }
    public int TranslatedLines { get; set; }
    public int FailedLines { get; set; }
    public double DurationSeconds { get; set; }
    public int? TokenUsagePrompt { get; set; }
    public int? TokenUsageCompletion { get; set; }
    public required string TranslationService { get; set; }
    
    public string? ApiCallsJson { get; set; }
    public string? LineResultsJson { get; set; }
    public string? TimingJson { get; set; }
    public string? PreviewJson { get; set; }
}