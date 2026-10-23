using System.ComponentModel.DataAnnotations.Schema;
using Lingarr.Core.Enum;

namespace Lingarr.Core.Entities;

/// <summary>
/// Represents an embedded subtitle stream detected within a media file.
/// Used to track what subtitles exist and their extraction status.
/// </summary>
public class EmbeddedSubtitle : BaseEntity
{
    /// <summary>
    /// FFmpeg stream index for extraction (e.g., 0 for first subtitle stream)
    /// </summary>
    public int StreamIndex { get; set; }
    
    /// <summary>
    /// ISO 639-2 language code (e.g., "eng", "jpn", "und" for undefined)
    /// </summary>
    public string? Language { get; set; }
    
    /// <summary>
    /// Stream title/description (e.g., "Signs & Songs", "Full Dialogue", "SDH")
    /// </summary>
    public string? Title { get; set; }
    
    /// <summary>
    /// FFmpeg codec name (e.g., "ass", "srt", "subrip", "webvtt", "hdmv_pgs_subtitle")
    /// </summary>
    public string CodecName { get; set; } = string.Empty;
    
    /// <summary>
    /// True for text-based subtitles (ASS, SRT, WebVTT), False for image-based (PGS, VobSub)
    /// </summary>
    public bool IsTextBased { get; set; }
    
    /// <summary>
    /// Whether this is marked as the default subtitle stream
    /// </summary>
    public bool IsDefault { get; set; }
    
    /// <summary>
    /// Whether this is a forced subtitle stream (typically for foreign language segments)
    /// </summary>
    public bool IsForced { get; set; }
    
    /// <summary>
    /// True if this subtitle has been extracted to an external file
    /// </summary>
    public bool IsExtracted { get; set; }
    
    /// <summary>
    /// Path to the extracted subtitle file (null if not extracted)
    /// </summary>
    public string? ExtractedPath { get; set; }

    /// <summary>
    /// Current OCR state for image-based embedded subtitles.
    /// </summary>
    public SubtitleOcrStatus OcrStatus { get; set; } = SubtitleOcrStatus.NotStarted;

    /// <summary>
    /// Path to OCR-generated SRT output, if available.
    /// </summary>
    public string? OcrExtractedPath { get; set; }

    /// <summary>
    /// Most recent OCR error message.
    /// </summary>
    public string? OcrError { get; set; }

    /// <summary>
    /// When OCR was last attempted.
    /// </summary>
    public DateTime? OcrAttemptedAt { get; set; }

    /// <summary>
    /// When OCR last completed successfully or with a quality verdict.
    /// </summary>
    public DateTime? OcrCompletedAt { get; set; }

    /// <summary>
    /// Number of cues parsed from the OCR output.
    /// </summary>
    public int? OcrCueCount { get; set; }

    /// <summary>
    /// Derived OCR quality score from 0 to 100.
    /// </summary>
    public int? OcrQualityScore { get; set; }

    /// <summary>
    /// Human-readable summary of OCR quality issues.
    /// </summary>
    public string? OcrIssueSummary { get; set; }

    /// <summary>
    /// When a user approved a low-quality OCR output for manual translation.
    /// </summary>
    public DateTime? OcrApprovedAt { get; set; }

    /// <summary>
    /// Language detected from the subtitle content when it contradicts the container tag,
    /// expressed as a normalized two letter code (e.g. "en"). Null when no high-confidence
    /// contradiction was found. The stored <see cref="Language"/> tag is never rewritten.
    /// </summary>
    public string? DetectedLanguage { get; set; }

    /// <summary>
    /// Confidence (0.0 to 1.0) of <see cref="DetectedLanguage"/>. Only set together with it.
    /// </summary>
    public double? DetectedLanguageConfidence { get; set; }

    /// <summary>
    /// How the last language check was produced, e.g. "script-ratio+stopwords" or "llm-batch".
    /// Also records negative verdicts such as "no-contradiction" or "insufficient-sample".
    /// </summary>
    public string? LanguageDetectionMethod { get; set; }

    /// <summary>
    /// Bounded human-readable evidence for the last language check.
    /// </summary>
    public string? LanguageDetectionEvidence { get; set; }

    /// <summary>
    /// When the language check last ran, successful or not. Used to cache negative results.
    /// </summary>
    public DateTime? LanguageCheckedAt { get; set; }

    /// <summary>
    /// Algorithm version used for the last check. Bumping the detector version invalidates
    /// stored results so they are re-evaluated on the next run.
    /// </summary>
    public int LanguageDetectionVersion { get; set; }

    // Foreign key for Episode (nullable - belongs to either Episode or Movie)
    public int? EpisodeId { get; set; }
    [ForeignKey(nameof(EpisodeId))]
    public Episode? Episode { get; set; }

    // Foreign key for Movie (nullable - belongs to either Episode or Movie)
    public int? MovieId { get; set; }
    [ForeignKey(nameof(MovieId))]
    public Movie? Movie { get; set; }
}
