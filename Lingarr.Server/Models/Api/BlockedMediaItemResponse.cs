using Lingarr.Core.Enum;

namespace Lingarr.Server.Models.Api;

/// <summary>
/// Response model for a media item currently blocked from translation.
/// Covers OCR quality-blocked streams (OcrBlocked), failed translations (Failed),
/// items needing re-analysis (Stale) and items waiting for a source subtitle (AwaitingSource).
/// </summary>
public class BlockedMediaItemResponse
{
    /// <summary>
    /// Id of the movie or episode.
    /// </summary>
    public int MediaId { get; set; }

    /// <summary>
    /// Media kind: "movie" or "episode".
    /// </summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>
    /// Display title of the media item.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Current translation state (OcrBlocked, Failed, Stale or AwaitingSource).
    /// Serialized as the enum name by the global JSON string enum converter.
    /// </summary>
    public TranslationState TranslationState { get; set; }

    /// <summary>
    /// Stream index of the blocked OCR stream (OcrBlocked items only).
    /// Null when no candidate stream could be resolved.
    /// </summary>
    public int? StreamIndex { get; set; }

    /// <summary>
    /// OCR status of the blocked stream (OcrBlocked items only).
    /// </summary>
    public SubtitleOcrStatus? OcrStatus { get; set; }

    /// <summary>
    /// OCR quality score of the blocked stream (OcrBlocked items only).
    /// </summary>
    public int? OcrQualityScore { get; set; }

    /// <summary>
    /// OCR quality threshold currently configured, so the UI can explain a verdict
    /// that was produced against an older threshold (OcrBlocked items only).
    /// </summary>
    public int? OcrMinimumQualityScore { get; set; }

    /// <summary>
    /// True when a quality-blocked OCR stream now meets the configured threshold,
    /// so a re-check promotes it without running OCR again (OcrBlocked items only).
    /// </summary>
    public bool OcrPassesCurrentThreshold { get; set; }

    /// <summary>
    /// Human-readable summary of the OCR quality issues. Re-derived against the
    /// configured threshold so text recorded under an older threshold is not shown
    /// as if it were current (OcrBlocked items only).
    /// </summary>
    public string? OcrIssueSummary { get; set; }

    /// <summary>
    /// Latest failure message of the failed translation request (Failed items only).
    /// </summary>
    public string? FailureMessage { get; set; }

    /// <summary>
    /// When the media directory was last checked while waiting for a source subtitle
    /// (AwaitingSource items only).
    /// </summary>
    public DateTime? LastSubtitleCheckAt { get; set; }

    /// <summary>
    /// When this item was last evaluated. For OCR-blocked items this is the last OCR
    /// verdict or attempt, for failed items the last failure, and otherwise the last
    /// subtitle check. An explicit re-check updates it.
    /// </summary>
    public DateTime? LastEvaluatedAt { get; set; }
}
