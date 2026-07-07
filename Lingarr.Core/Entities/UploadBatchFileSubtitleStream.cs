namespace Lingarr.Core.Entities;

public class UploadBatchFileSubtitleStream : BaseEntity
{
    public required int UploadBatchFileId { get; set; }
    public required UploadBatchFile UploadBatchFile { get; set; }
    public required int StreamIndex { get; set; }
    public string? Language { get; set; }
    public string? Title { get; set; }
    public required string CodecName { get; set; }
    public bool IsTextBased { get; set; }
    public bool IsDefault { get; set; }
    public bool IsForced { get; set; }
}
