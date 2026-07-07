namespace Lingarr.Core.Enum;

/// <summary>
/// Represents the OCR processing state for image-based embedded subtitles.
/// </summary>
public enum SubtitleOcrStatus
{
    NotStarted = 0,
    Queued = 1,
    Processing = 2,
    Succeeded = 3,
    BlockedLowQuality = 4,
    Failed = 5,
    Approved = 6
}
