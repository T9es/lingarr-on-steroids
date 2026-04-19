using Lingarr.Core.Enum;

namespace Lingarr.Core.Entities;

public class UploadArtifact : BaseEntity
{
    public required int UploadBatchId { get; set; }
    public required UploadBatch UploadBatch { get; set; }
    public int? UploadBatchFileId { get; set; }
    public UploadBatchFile? UploadBatchFile { get; set; }
    public required UploadArtifactKind Kind { get; set; }
    public required string FileName { get; set; }
    public required string Path { get; set; }
    public required string RelativePath { get; set; }
    public long FileSizeBytes { get; set; }
    public string? ContentType { get; set; }
    public bool IsDownloadable { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
