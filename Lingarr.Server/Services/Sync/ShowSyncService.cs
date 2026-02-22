using System.Text.Json;
using Lingarr.Core.Configuration;
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
    private readonly ISettingService _settingService;
    private readonly ILogger<ShowSyncService> _logger;

    public ShowSyncService(
        LingarrDbContext dbContext,
        IShowSync showSync,
        ISeasonSync seasonSync,
        IEpisodeSync episodeSync,
        ISettingService settingService,
        ILogger<ShowSyncService> logger)
    {
        _dbContext = dbContext;
        _showSync = showSync;
        _seasonSync = seasonSync;
        _episodeSync = episodeSync;
        _settingService = settingService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SyncShows(List<(SonarrShow Show, string InstanceId)> shows)
    {
        // Pre-fetch all unique instance configs ONCE to avoid N+1 queries
        var uniqueInstanceIds = shows.Select(s => s.InstanceId).Distinct().ToList();
        var instanceConfigs = new Dictionary<string, (string Url, string ApiKey)>();
        
        foreach (var instanceId in uniqueInstanceIds)
        {
            instanceConfigs[instanceId] = await GetSonarrInstanceConfig(instanceId);
        }
        
        var processedCount = 0;
        
        foreach (var (show, instanceId) in shows)
        {
            var (instanceUrl, instanceApiKey) = instanceConfigs[instanceId];  // O(1) lookup
            var showEntity = await _showSync.SyncShow(show, instanceId);

            foreach (var season in show.Seasons)
            {
                var seasonEntity = await _seasonSync.SyncSeason(showEntity, show, season, instanceUrl, instanceApiKey);
                await _episodeSync.SyncEpisodes(show, seasonEntity, instanceUrl, instanceApiKey);
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
        var (instanceUrl, instanceApiKey) = await GetSonarrInstanceConfig(instanceId);
        var showEntity = await _showSync.SyncShow(show, instanceId);

        foreach (var season in show.Seasons)
        {
            var seasonEntity = await _seasonSync.SyncSeason(showEntity, show, season, instanceUrl, instanceApiKey);
            await _episodeSync.SyncEpisodes(show, seasonEntity, instanceUrl, instanceApiKey);
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

    /// <summary>
    /// Gets the Sonarr instance URL and API key from the instance ID
    /// </summary>
    /// <param name="instanceId">The instance ID to look up</param>
    /// <returns>A tuple containing the instance URL and API key</returns>
    private async Task<(string Url, string ApiKey)> GetSonarrInstanceConfig(string instanceId)
    {
        var instancesJson = await _settingService.GetSetting(SettingKeys.Integration.SonarrInstances);
        
        if (!string.IsNullOrEmpty(instancesJson))
        {
            try
            {
                var instances = JsonSerializer.Deserialize<List<SonarrInstance>>(instancesJson);
                var instance = instances?.FirstOrDefault(i => i.Id == instanceId);
                if (instance != null)
                {
                    return (instance.Url, instance.ApiKey);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize Sonarr instances from settings");
            }
        }

        // Fall back to single instance settings
        var url = await _settingService.GetSetting(SettingKeys.Integration.SonarrUrl);
        var apiKey = await _settingService.GetSetting(SettingKeys.Integration.SonarrApiKey);
        
        return (url ?? string.Empty, apiKey ?? string.Empty);
    }
}