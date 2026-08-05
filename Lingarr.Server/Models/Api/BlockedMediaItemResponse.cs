using Lingarr.Core.Enum;

namespace Lingarr.Server.Models.Api;

/// <summary>
/// Response model for a media item currently blocked from translation.
/// Covers OCR quality-blocked streams (OcrBlocked), items needing re-analysis (Stale)
/// and items waiting for a source subtitle (AwaitingSource).
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
    /// Current translation state (OcrBlocked, Stale or AwaitingSource).
    /// </summary>
    public TranslationState TranslationState { get; set; }

    /// <summary>
    /// Stream index of the blocked OCR stream (OcrBlocked items only).
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
    /// Human-readable summary of the OCR quality issues (OcrBlocked items only).
    /// </summary>
    public string? OcrIssueSummary { get; set; }

    /// <summary>
    /// When the media directory was last checked while waiting for a source subtitle
    /// (AwaitingSource items only).
    /// </summary>
    public DateTime? LastSubtitleCheckAt { get; set; }
}
