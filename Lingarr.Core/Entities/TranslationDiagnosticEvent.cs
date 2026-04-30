using Lingarr.Core.Enum;

namespace Lingarr.Core.Entities;

public class TranslationDiagnosticEvent : BaseEntity
{
    public int? TranslationRequestId { get; set; }
    public int? MediaId { get; set; }
    public MediaType? MediaType { get; set; }
    public string? Title { get; set; }
    public required string Stage { get; set; }
    public string? Provider { get; set; }
    public string? SourcePath { get; set; }
    public string? TargetPath { get; set; }
    public string? QuarantinePath { get; set; }
    public string? OutputFormat { get; set; }
    public string? SourceSnapshotIdentity { get; set; }
    public string? SourceSnapshotFingerprint { get; set; }
    public required string ReasonCode { get; set; }
    public required string Summary { get; set; }
    public string SampleLinesJson { get; set; } = "[]";
    public string? DetailsJson { get; set; }
    public DateTime ExpiresAt { get; set; }
}
