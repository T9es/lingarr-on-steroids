namespace Lingarr.Server.Models.UploadWorkspace;

public class UploadChunkSessionResponse
{
    public required Guid UploadId { get; set; }
    public required string FileName { get; set; }
    public required long FileSizeBytes { get; set; }
    public string? ContentType { get; set; }
    public DateTime? LastModifiedUtc { get; set; }
    public int ChunkSizeBytes { get; set; }
    public int MaxChunkSizeBytes { get; set; }
    public int ExpectedChunks { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public int UploadedChunkCount { get; set; }
    public long UploadedBytes { get; set; }
}
