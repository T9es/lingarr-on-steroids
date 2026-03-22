using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;

namespace Lingarr.Server.Interfaces.Services;

/// <summary>
/// Coordinates automation processing for both webhook-triggered items and fallback schedule sweeps.
/// </summary>
public interface IAutomationService
{
    /// <summary>
    /// Processes a single media item by ID using automation rules.
    /// Intended for webhook-triggered flows.
    /// </summary>
    Task<int> ProcessSingleMediaForAutomationAsync(int mediaId, MediaType mediaType, string triggerSource);

    /// <summary>
    /// Processes a loaded media item using automation rules.
    /// Intended for fallback schedule sweeps where candidate media is already loaded.
    /// </summary>
    Task<int> ProcessLoadedMediaForAutomationAsync(
        IMedia media,
        MediaType mediaType,
        string triggerSource,
        bool updateRotationTimestamp = false,
        bool forceStateRefresh = false);
}
