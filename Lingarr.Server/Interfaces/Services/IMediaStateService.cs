using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;

namespace Lingarr.Server.Interfaces.Services;

/// <summary>
/// Service for managing translation state of media items.
/// Centralizes all logic for determining what needs translation.
/// </summary>
public interface IMediaStateService
{
    /// <summary>
    /// Computes and persists the TranslationState for a media item.
    /// Call after: indexing, translation completion, settings change, or manual request.
    /// </summary>
    /// <param name="media">The media item to update</param>
    /// <param name="mediaType">Type of media (Movie or Episode)</param>
    /// <returns>The computed translation state</returns>
    Task<TranslationState> UpdateStateAsync(IMedia media, MediaType mediaType);
    
    /// <summary>
    /// Marks all media as Stale. Used when language settings change.
    /// Does NOT recompute states - just sets TranslationState = Stale.
    /// States will be recomputed on next access or automation run.
    /// </summary>
    Task MarkAllStaleAsync();
    
    /// <summary>
    /// Gets media items that need translation work.
    /// Returns items where TranslationState is Pending, Stale, or Unknown (with IndexedAt set).
    /// Excludes items with active TranslationRequests.
    /// </summary>
    /// <param name="limit">Maximum items to return</param>
    /// <param name="priorityFirst">If true, priority items are returned first</param>
    /// <returns>List of media items needing work</returns>
    Task<List<(IMedia Media, MediaType Type)>> GetMediaNeedingTranslationAsync(int limit, bool priorityFirst = true);
    
    /// <summary>
    /// Gets the current language settings version number.
    /// </summary>
    Task<int> GetSettingsVersionAsync();
    
    /// <summary>
    /// Increments the language settings version.
    /// Call when source_languages, target_languages, or ignore_captions change.
    /// </summary>
    Task IncrementSettingsVersionAsync();
    
    /// <summary>
    /// Checks if a media item has any active (Pending/InProgress) translation requests.
    /// </summary>
    Task<bool> HasActiveTranslationRequestAsync(int mediaId, MediaType mediaType);
}
