namespace Lingarr.Core.Enum;

/// <summary>
/// Represents the translation status of a media item.
/// Used for efficient querying to find media needing translation work.
/// </summary>
public enum TranslationState
{
    /// <summary>
    /// Not yet analyzed. Default for new/legacy items.
    /// Will be analyzed on next automation run or sync.
    /// </summary>
    Unknown = 0,
    
    /// <summary>
    /// No translation possible or needed.
    /// Either excluded, no source subtitles, or source language not configured.
    /// </summary>
    NotApplicable = 1,
    
    /// <summary>
    /// Ready for translation.
    /// Has source subtitle in configured language, missing one or more target languages.
    /// </summary>
    Pending = 2,
    
    /// <summary>
    /// Translation in progress.
    /// Has an active TranslationRequest (Pending or InProgress status).
    /// </summary>
    InProgress = 3,
    
    /// <summary>
    /// All translations complete.
    /// Has subtitles for all configured target languages.
    /// </summary>
    Complete = 4,
    
    /// <summary>
    /// Needs re-analysis.
    /// Language settings changed since last state computation.
    /// </summary>
    Stale = 5,
    
    /// <summary>
    /// No suitable subtitle tracks available.
    /// All embedded subtitle tracks have fewer than the minimum required entries (sparse/Signs/Songs only).
    /// </summary>
    NoSuitableSubtitles = 6,

    /// <summary>
    /// Translation failed.
    /// A previous translation request for this media failed and needs manual intervention.
    /// </summary>
    Failed = 7,

    /// <summary>
    /// Waiting for source subtitle to become available.
    /// Configured for translation but no source subtitle exists yet.
    /// Will be re-checked during sync when directory mtime changes.
    /// </summary>
    AwaitingSource = 8
}
