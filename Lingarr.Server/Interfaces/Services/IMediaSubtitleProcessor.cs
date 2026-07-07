using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Models;

namespace Lingarr.Server.Interfaces.Services;

public interface IMediaSubtitleProcessor
{
    /// <summary>
    /// Processes the subtitles for a given media item.
    /// </summary>
    /// <param name="media">The media item to process subtitles for.</param>
    /// <param name="mediaType">The type of the media (e.g., Movie, Episode).</param>
    /// <returns>
    /// A boolean indicating whether new subtitle processing was initiated.
    /// Returns true if new translations were requested, false if no processing was needed or possible.
    /// </returns>
    Task<bool> ProcessMedia(IMedia media, MediaType mediaType);

    /// <summary>
    /// Processes the subtitles for a given media item with option to force processing.
    /// </summary>
    /// <param name="media">The media item to process subtitles for.</param>
    /// <param name="mediaType">The type of the media (e.g., Movie, Episode).</param>
    /// <param name="forceProcess">If true, bypasses the media hash check and always processes.</param>
    /// <param name="forceTranslation">If true, queues translations even when outputs already exist.</param>
    /// <param name="forcePriority">If true, forces jobs to use the priority queue regardless of media priority status.</param>
    /// <param name="queueTranslations">If false, reports queueable translations without creating requests.</param>
    /// <param name="maxTranslationsToQueue">Optional maximum number of requests to create.</param>
    /// <returns>
    /// The number of translation requests that were queued.
    /// </returns>
    Task<int> ProcessMediaForceAsync(
        IMedia media,
        MediaType mediaType,
        bool forceProcess = true,
        bool forceTranslation = true,
        bool forcePriority = false,
        bool queueTranslations = true,
        int? maxTranslationsToQueue = null);

    /// <summary>
    /// Processes subtitles and appends reportable integrity findings for callers that need details.
    /// </summary>
    /// <param name="media">The media item to process subtitles for.</param>
    /// <param name="mediaType">The type of the media.</param>
    /// <param name="forceProcess">If true, bypasses the media hash check and always processes.</param>
    /// <param name="forceTranslation">If true, queues translations even when outputs already exist.</param>
    /// <param name="forcePriority">If true, forces jobs to use the priority queue regardless of media priority status.</param>
    /// <param name="queueTranslations">If false, reports queueable translations without creating requests.</param>
    /// <param name="maxTranslationsToQueue">Optional maximum number of requests to create.</param>
    /// <param name="integrityFindings">Collection that receives detailed integrity findings.</param>
    /// <returns>The number of translation requests queued or queueable targets reported.</returns>
    Task<int> ProcessMediaForceAsync(
        IMedia media,
        MediaType mediaType,
        bool forceProcess,
        bool forceTranslation,
        bool forcePriority,
        bool queueTranslations,
        int? maxTranslationsToQueue,
        ICollection<SubtitleIntegrityFinding> integrityFindings);

    /// <summary>
    /// Processes subtitles for a single requested target language.
    /// </summary>
    /// <param name="media">The media item to process subtitles for.</param>
    /// <param name="mediaType">The type of the media.</param>
    /// <param name="targetLanguage">The target language to queue or report.</param>
    /// <param name="forceProcess">If true, bypasses the media hash check and always processes.</param>
    /// <param name="forceTranslation">If true, queues translation even when output already exists.</param>
    /// <param name="forcePriority">If true, forces jobs to use the priority queue regardless of media priority status.</param>
    /// <param name="queueTranslations">If false, reports queueable translations without creating requests.</param>
    /// <param name="maxTranslationsToQueue">Optional maximum number of requests to create.</param>
    /// <returns>The number of translation requests queued or queueable targets reported.</returns>
    Task<int> ProcessMediaForceTargetAsync(
        IMedia media,
        MediaType mediaType,
        string targetLanguage,
        bool forceProcess = true,
        bool forceTranslation = true,
        bool forcePriority = false,
        bool queueTranslations = true,
        int? maxTranslationsToQueue = null);
}
