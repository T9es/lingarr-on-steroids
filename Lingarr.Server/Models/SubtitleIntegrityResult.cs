namespace Lingarr.Server.Models;

/// <summary>
/// Detailed result of comparing a translated subtitle against its selected source.
/// </summary>
public class SubtitleIntegrityCheckResult
{
    public bool IsValid { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int? SourceEntryCount { get; set; }
    public int? TargetEntryCount { get; set; }
    public int? MinimumTargetEntryCount { get; set; }
}

/// <summary>
/// A single actionable finding from a bulk subtitle integrity scan.
/// </summary>
public class SubtitleIntegrityFinding
{
    public int MediaId { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string MediaTitle { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public string SourceRole { get; set; } = "primary";
    public string Reason { get; set; } = string.Empty;
    public string? SourcePath { get; set; }
    public string? TargetPath { get; set; }
    public int? SourceEntries { get; set; }
    public int? TargetEntries { get; set; }
    public int? MinimumTargetEntries { get; set; }
    public string? SourceSnapshotType { get; set; }
    public string? SourceSnapshotIdentity { get; set; }
    public int? SourceSnapshotStreamIndex { get; set; }
    public bool IsQueued { get; set; }
    public bool Dismissed { get; set; }
}
