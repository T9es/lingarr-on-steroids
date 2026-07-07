namespace Lingarr.Server.Models.UploadWorkspace;

public class UploadChunkResponse
{
    public required Guid UploadId { get; set; }
    public int ChunkIndex { get; set; }
    public long ChunkSizeBytes { get; set; }
    public int UploadedChunkCount { get; set; }
    public long UploadedBytes { get; set; }
    public long FileSizeBytes { get; set; }
    public bool IsComplete { get; set; }
}
