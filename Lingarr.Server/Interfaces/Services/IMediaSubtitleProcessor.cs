using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;

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
    /// <param name="forcePriority">If true, forces jobs to use the priority queue regardless of media priority status.</param>
    /// <returns>
    /// The number of translation requests that were queued.
    /// </returns>
    Task<int> ProcessMediaForceAsync(
        IMedia media, 
        MediaType mediaType,
        bool forceProcess = true, 
        bool forceTranslation = true, 
        bool forcePriority = false);
}
