using Lingarr.Core.Entities;
using Lingarr.Server.Models.Integrations;

namespace Lingarr.Server.Interfaces.Services.Sync;

public interface IShowSync
{
    /// <summary>
    /// Synchronizes a single show from Sonarr
    /// </summary>
    /// <param name="show">The Sonarr show to sync</param>
    /// <param name="instanceId">The ID of the Sonarr instance this show belongs to</param>
    /// <returns>The synchronized show entity</returns>
    Task<Show> SyncShow(SonarrShow show, string instanceId);
}