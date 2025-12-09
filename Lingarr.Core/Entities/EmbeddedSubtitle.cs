using System.ComponentModel.DataAnnotations.Schema;

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

    // Foreign key for Episode (nullable - belongs to either Episode or Movie)
    public int? EpisodeId { get; set; }
    [ForeignKey(nameof(EpisodeId))]
    public Episode? Episode { get; set; }

    // Foreign key for Movie (nullable - belongs to either Episode or Movie)
    public int? MovieId { get; set; }
    [ForeignKey(nameof(MovieId))]
    public Movie? Movie { get; set; }
}
