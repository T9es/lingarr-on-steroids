using Lingarr.Core.Enum;

namespace Lingarr.Server.Models.UploadWorkspace;

public class UploadArtifactResponse
{
    public required int Id { get; set; }
    public int? UploadBatchFileId { get; set; }
    public required UploadArtifactKind Kind { get; set; }
    public required string FileName { get; set; }
    public long FileSizeBytes { get; set; }
    public string? ContentType { get; set; }
    public bool IsDownloadable { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public required string DownloadUrl { get; set; }
}
