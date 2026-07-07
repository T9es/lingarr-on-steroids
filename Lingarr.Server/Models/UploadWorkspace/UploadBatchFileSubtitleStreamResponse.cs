namespace Lingarr.Server.Models.UploadWorkspace;

public class UploadBatchFileSubtitleStreamResponse
{
    public required int Id { get; set; }
    public required int StreamIndex { get; set; }
    public string? Language { get; set; }
    public string? Title { get; set; }
    public required string CodecName { get; set; }
    public bool IsTextBased { get; set; }
    public bool IsDefault { get; set; }
    public bool IsForced { get; set; }
}
