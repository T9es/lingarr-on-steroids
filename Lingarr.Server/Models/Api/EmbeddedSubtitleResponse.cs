namespace Lingarr.Server.Models.Api;

using Lingarr.Core.Enum;

/// <summary>
/// Response model for embedded subtitle information
/// </summary>
public class EmbeddedSubtitleResponse
{
    public int Id { get; set; }
    public int StreamIndex { get; set; }
    public string? Language { get; set; }
    public string? Title { get; set; }
    public string CodecName { get; set; } = "";
    public bool IsTextBased { get; set; }
    public bool IsDefault { get; set; }
    public bool IsForced { get; set; }
    public bool IsExtracted { get; set; }
    public string? ExtractedPath { get; set; }
    public SubtitleOcrStatus OcrStatus { get; set; }
    public string? OcrExtractedPath { get; set; }
    public string? OcrError { get; set; }
    public DateTime? OcrAttemptedAt { get; set; }
    public DateTime? OcrCompletedAt { get; set; }
    public int? OcrCueCount { get; set; }
    public int? OcrQualityScore { get; set; }
    public string? OcrIssueSummary { get; set; }
    public DateTime? OcrApprovedAt { get; set; }
    public bool IsOcrSupported { get; set; }
    public bool IsOcrUsable { get; set; }
}

/// <summary>
/// Response model for subtitle extraction result
/// </summary>
public class ExtractSubtitleResponse
{
    public bool Success { get; set; }
    public string? ExtractedPath { get; set; }
    public string? Error { get; set; }
}
