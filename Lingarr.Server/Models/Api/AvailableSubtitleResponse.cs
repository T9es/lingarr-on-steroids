namespace Lingarr.Server.Models.Api;

using Lingarr.Core.Enum;

/// <summary>
/// Response model for available subtitle information with entry count
/// </summary>
public class AvailableSubtitleResponse
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
    /// <summary>
    /// Number of dialogue entries in the subtitle file (if extracted)
    /// </summary>
    public int? EntryCount { get; set; }
    /// <summary>
    /// Whether this subtitle is considered sparse (below minimum entry threshold)
    /// </summary>
    public bool? IsSparse { get; set; }
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

    /// <summary>
    /// Language detected from the subtitle content when it contradicts the container tag.
    /// Null when no high confidence contradiction was found. The tag itself is unchanged.
    /// </summary>
    public string? DetectedLanguage { get; set; }

    /// <summary>
    /// Confidence (0.0 to 1.0) of <see cref="DetectedLanguage"/>.
    /// </summary>
    public double? DetectedLanguageConfidence { get; set; }

    /// <summary>
    /// How the last language check was produced, including negative verdicts such as
    /// "no-contradiction", "insufficient-sample" or "ambiguous-latin".
    /// </summary>
    public string? LanguageDetectionMethod { get; set; }

    /// <summary>
    /// Human readable evidence or reason for the last language check.
    /// </summary>
    public string? LanguageDetectionEvidence { get; set; }

    /// <summary>
    /// When the language check last ran (successful or not).
    /// </summary>
    public DateTime? LanguageCheckedAt { get; set; }
}

/// <summary>
/// Request model for queuing translation with a specific subtitle stream
/// </summary>
public class QueueWithSubtitleRequest
{
    /// <summary>
    /// The ID of the media item to translate
    /// </summary>
    public int MediaId { get; set; }
    
    /// <summary>
    /// The type of media (Movie or Episode)
    /// </summary>
    public string MediaType { get; set; } = "";
    
    /// <summary>
    /// The stream index of the subtitle to use
    /// </summary>
    public int StreamIndex { get; set; }
    
    /// <summary>
    /// Source language code
    /// </summary>
    public string SourceLanguage { get; set; } = "";
}

/// <summary>
/// Response model for queue with subtitle endpoint
/// </summary>
public class QueueWithSubtitleResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int TranslationsQueued { get; set; }
}
