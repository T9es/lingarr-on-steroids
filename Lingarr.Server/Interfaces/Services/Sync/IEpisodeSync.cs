using Lingarr.Core.Entities;
using Lingarr.Server.Models.Integrations;
using Lingarr.Server.Models.Sync;

namespace Lingarr.Server.Interfaces.Services.Sync;

public interface IEpisodeSync
{
    /// <summary>
    /// Synchronizes episodes for a given show and season
    /// </summary>
    /// <param name="show">The Sonarr show containing the episodes</param>
    /// <param name="season">The season to sync episodes for</param>
    /// <param name="instanceId">The Sonarr instance identifier</param>
    /// <param name="instanceUrl">The Sonarr instance URL</param>
    /// <param name="instanceApiKey">The Sonarr instance API key</param>
    /// <returns>A task representing the asynchronous operation</returns>
    Task SyncEpisodes(SonarrShow show, Season season, string instanceId, string instanceUrl, string instanceApiKey);

    /// <summary>
    /// Synchronizes a single episode for targeted webhook refresh processing.
    /// </summary>
    /// <param name="show">The Sonarr show containing the episode</param>
    /// <param name="episode">The episode to sync</param>
    /// <param name="season">The season entity the episode belongs to</param>
    /// <param name="instanceId">The Sonarr instance identifier</param>
    /// <param name="instanceUrl">The Sonarr instance URL</param>
    /// <param name="instanceApiKey">The Sonarr instance API key</param>
    /// <returns>The targeted episode refresh result, or null if the episode has no usable file</returns>
    Task<EpisodeRefreshResult?> SyncEpisode(
        SonarrShow show,
        SonarrEpisode episode,
        Season season,
        string instanceId,
        string instanceUrl,
        string instanceApiKey);
}
