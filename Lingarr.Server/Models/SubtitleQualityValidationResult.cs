namespace Lingarr.Server.Models;

public class SubtitleQualityValidationResult
{
    public bool IsValid { get; set; }
    public string Summary { get; set; } = string.Empty;
    public int SourceEntryCount { get; set; }
    public int TargetEntryCount { get; set; }
    public int MinimumTargetEntryCount { get; set; }
    public List<string> IssueTypes { get; set; } = new();
    public List<string> SampleLines { get; set; } = new();
}
