using Lingarr.Core.Enum;

namespace Lingarr.Core.Entities;

public class UploadBatch : BaseEntity
{
    public required string Name { get; set; }
    public required string TargetLanguage { get; set; }
    public required string StoragePath { get; set; }
    public UploadBatchStatus Status { get; set; } = UploadBatchStatus.Draft;
    public bool DefaultRemuxEnabled { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? FailureReason { get; set; }
    public List<UploadBatchFile> Files { get; set; } = [];
    public List<UploadArtifact> Artifacts { get; set; } = [];
}
