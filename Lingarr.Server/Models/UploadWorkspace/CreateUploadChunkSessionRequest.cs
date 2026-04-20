namespace Lingarr.Server.Models.UploadWorkspace;

public class CreateUploadChunkSessionRequest
{
    public required string FileName { get; set; }
    public required long FileSizeBytes { get; set; }
    public string? ContentType { get; set; }
    public DateTime? LastModifiedUtc { get; set; }
}
