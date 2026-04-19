using Lingarr.Core.Enum;

namespace Lingarr.Server.Models.UploadWorkspace;

public class UploadBatchResponse
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public required string TargetLanguage { get; set; }
    public required UploadBatchStatus Status { get; set; }
    public bool DefaultRemuxEnabled { get; set; }
    public int FileCount { get; set; }
    public int CompletedFileCount { get; set; }
    public int FailedFileCount { get; set; }
    public int ActiveFileCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? FailureReason { get; set; }
    public required List<UploadBatchFileResponse> Files { get; set; }
    public required List<UploadArtifactResponse> Artifacts { get; set; }
}
