namespace Lingarr.Server.Models;

public class SubtitleQualityAuditResult
{
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public bool ReportOnly { get; set; } = true;
    public int CompletedRequestsScanned { get; set; }
    public int FilesScanned { get; set; }
    public int MissingOutputs { get; set; }
    public int CacheOnlyOutputs { get; set; }
    public List<SubtitleQualityAuditFinding> Findings { get; set; } = new();
}

public class SubtitleQualityAuditFinding
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int? TranslationRequestId { get; set; }
    public int MediaId { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string MediaTitle { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string? OutputFormat { get; set; }
    public int SourceEntryCount { get; set; }
    public int TargetEntryCount { get; set; }
    public int MinimumTargetEntryCount { get; set; }
    public List<string> IssueTypes { get; set; } = new();
    public string IssueSummary { get; set; } = string.Empty;
    public List<string> SampleLines { get; set; } = new();
    public bool IsQueued { get; set; }
    public bool Dismissed { get; set; }
}
