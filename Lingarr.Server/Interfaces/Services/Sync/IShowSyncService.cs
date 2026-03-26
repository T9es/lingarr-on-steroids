using Lingarr.Core.Entities;
using Lingarr.Server.Models.Integrations;
using Lingarr.Server.Models.Sync;

namespace Lingarr.Server.Interfaces.Services.Sync;

public interface IShowSyncService
{
    /// <summary>
    /// Synchronizes multiple shows from Sonarr with their instance IDs
    /// </summary>
    /// <param name="shows">The list of shows with their instance IDs to sync</param>
    /// <returns>A task representing the asynchronous operation</returns>
    Task SyncShows(List<(SonarrShow Show, string InstanceId)> shows);

    /// <summary>
    /// Synchronizes a show from Sonarr
    /// </summary>
    /// <param name="show">The Sonarr show to sync</param>
    /// <param name="instanceId">The ID of the Sonarr instance this show belongs to</param>
    /// <returns>A task representing the asynchronous operation</returns>
    Task<Show?> SyncShow(SonarrShow show, string instanceId);

    /// <summary>
    /// Synchronizes a single episode from Sonarr, refreshing only the affected show metadata,
    /// season path, and episode state needed for webhook processing.
    /// </summary>
    /// <param name="episode">The Sonarr episode to sync</param>
    /// <param name="instanceId">The ID of the Sonarr instance this episode belongs to</param>
    /// <returns>The targeted episode refresh result, or null if sync could not complete</returns>
    Task<EpisodeRefreshResult?> SyncEpisode(SonarrEpisode episode, string instanceId);

    /// <summary>
    /// Removes shows that no longer exist in Sonarr for a specific instance
    /// </summary>
    /// <param name="existingSonarrIds">The collection of currently existing Sonarr show IDs</param>
    /// <param name="instanceId">The ID of the Sonarr instance to filter by</param>
    /// <returns>A task representing the asynchronous operation</returns>
    Task RemoveNonExistentShows(IEnumerable<int> existingSonarrIds, string instanceId);
}
