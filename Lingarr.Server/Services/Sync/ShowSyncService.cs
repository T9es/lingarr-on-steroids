using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Sync;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.Sync;

public class ShowSyncService : IShowSyncService
{
    private const int BatchSize = 100;
    
    private readonly LingarrDbContext _dbContext;
    private readonly IShowSync _showSync;
    private readonly ISeasonSync _seasonSync;
    private readonly IEpisodeSync _episodeSync;
    private readonly IInstanceConfigService _instanceConfigService;
    private readonly ILogger<ShowSyncService> _logger;

    public ShowSyncService(
        LingarrDbContext dbContext,
        IShowSync showSync,
        ISeasonSync seasonSync,
        IEpisodeSync episodeSync,
        IInstanceConfigService instanceConfigService,
        ILogger<ShowSyncService> logger)
    {
        _dbContext = dbContext;
        _showSync = showSync;
        _seasonSync = seasonSync;
        _episodeSync = episodeSync;
        _instanceConfigService = instanceConfigService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SyncShows(List<(SonarrShow Show, string InstanceId)> shows)
    {
        var uniqueInstanceIds = shows.Select(s => s.InstanceId).Distinct().ToList();
        var instanceConfigs = new Dictionary<string, InstanceConfig>();
        
        foreach (var instanceId in uniqueInstanceIds)
        {
            var config = await _instanceConfigService.GetSonarrConfig(instanceId);
            if (config != null)
            {
                instanceConfigs[instanceId] = config;
            }
        }
        
        var processedCount = 0;
        
        foreach (var (show, instanceId) in shows)
        {
            if (!instanceConfigs.TryGetValue(instanceId, out var config))
            {
                _logger.LogWarning("Could not find Sonarr instance config for {InstanceId}, skipping", instanceId);
                continue;
            }
            
            var showEntity = await _showSync.SyncShow(show, instanceId);

            foreach (var season in show.Seasons)
            {
                var seasonEntity = await _seasonSync.SyncSeason(showEntity, show, season, config.Url, config.ApiKey);
                await _episodeSync.SyncEpisodes(show, seasonEntity, instanceId, config.Url, config.ApiKey);
            }
            
            processedCount++;

            if (processedCount % BatchSize == 0)
            {
                await SaveChanges(processedCount, shows.Count);
            }
        }

        if (processedCount % BatchSize != 0)
        {
            await SaveChanges(processedCount, shows.Count);
        }
    }

    /// <inheritdoc />
    public async Task<Show?> SyncShow(SonarrShow show, string instanceId)
    {
        var config = await _instanceConfigService.GetSonarrConfig(instanceId);
        if (config == null)
        {
            _logger.LogWarning("Could not find Sonarr instance config for {InstanceId}", instanceId);
            return null;
        }
        
        var showEntity = await _showSync.SyncShow(show, instanceId);

        foreach (var season in show.Seasons)
        {
            var seasonEntity = await _seasonSync.SyncSeason(showEntity, show, season, config.Url, config.ApiKey);
            await _episodeSync.SyncEpisodes(show, seasonEntity, instanceId, config.Url, config.ApiKey);
        }

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Synced a single show");

        return showEntity;
    }

    /// <inheritdoc />
    public async Task RemoveNonExistentShows(IEnumerable<int> existingSonarrIds, string instanceId)
    {
        var showsToDelete = await _dbContext.Shows
            .Include(s => s.Images)
            .Include(s => s.Seasons)
                .ThenInclude(s => s.Episodes)
                    .ThenInclude(e => e.EmbeddedSubtitles)
            .Where(s => s.SourceInstanceId == instanceId)
            .Where(s => !existingSonarrIds.Contains(s.SonarrId))
            .ToListAsync();

        if (showsToDelete.Any())
        {
            _logger.LogInformation("Removing {Count} shows that no longer exist in Sonarr instance '{InstanceId}'", 
                showsToDelete.Count, instanceId);

            var episodes = showsToDelete.SelectMany(s => s.Seasons.SelectMany(season => season.Episodes)).ToList();
            var embeddedSubtitles = episodes.SelectMany(e => e.EmbeddedSubtitles).ToList();
            var seasons = showsToDelete.SelectMany(s => s.Seasons).ToList();
            var images = showsToDelete.SelectMany(s => s.Images).ToList();

            _dbContext.EmbeddedSubtitles.RemoveRange(embeddedSubtitles);
            _dbContext.Episodes.RemoveRange(episodes);
            _dbContext.Seasons.RemoveRange(seasons);
            _dbContext.Images.RemoveRange(images);
            _dbContext.Shows.RemoveRange(showsToDelete);

            await _dbContext.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Saves pending changes to the database and logs the sync progress
    /// </summary>
    /// <param name="processedCount">The number of shows processed so far</param>
    /// <param name="totalCount">The total number of shows to process</param>
    /// <returns>A task representing the asynchronous operation</returns>
    private async Task SaveChanges(int processedCount, int totalCount)
    {
        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Synced and saved {ProcessedCount} out of {TotalCount} shows", 
            processedCount, totalCount);
    }

}