namespace Lingarr.Core.Enum;

public enum UploadBatchFileStatus
{
    Uploaded,
    NeedsConfiguration,
    Ready,
    Queued,
    Processing,
    Completed,
    Failed,
    Cancelled
}
