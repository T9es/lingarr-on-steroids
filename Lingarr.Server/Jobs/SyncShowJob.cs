using Hangfire;
using Microsoft.EntityFrameworkCore;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Enum;
using Lingarr.Server.Filters;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Integration;
using Lingarr.Server.Interfaces.Services.Sync;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Integrations;
using Microsoft.OpenApi.Extensions;
using System.Text.Json;

namespace Lingarr.Server.Jobs;

public class SyncShowJob
{
    private readonly LingarrDbContext _dbContext;
    private readonly ISonarrService _sonarrService;
    private readonly ILogger<SyncShowJob> _logger;
    private readonly IScheduleService _scheduleService;
    private readonly IShowSyncService _showSyncService;
    private readonly ISettingService _settingService;

    public SyncShowJob(
        LingarrDbContext dbContext,
        ISonarrService sonarrService,
        ILogger<SyncShowJob> logger,
        IScheduleService scheduleService,
        IShowSyncService showSyncService,
        ISettingService settingService)
    {
        _dbContext = dbContext;
        _sonarrService = sonarrService;
        _logger = logger;
        _scheduleService = scheduleService;
        _showSyncService = showSyncService;
        _settingService = settingService;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    [AutomaticRetry(Attempts = 0)]
    [Queue("shows")]
    public async Task Execute()
    {
        var jobName = JobContextFilter.GetCurrentJobTypeName();
        _logger.LogInformation("Sonarr sync job initiated");

        try
        {
            await _scheduleService.UpdateJobState(jobName, JobStatus.Processing.GetDisplayName());

            // Get instances from settings
            var instances = await GetSonarrInstances();
            if (instances.Count == 0)
            {
                _logger.LogWarning("No Sonarr instances configured");
                await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
                return;
            }

            var allShows = new List<(SonarrShow Show, string InstanceId)>();
            var instanceShowIds = new Dictionary<string, HashSet<int>>();

            foreach (var instance in instances)
            {
                try
                {
                    var shows = await _sonarrService.GetShows(instance.Url, instance.ApiKey);
                    if (shows != null)
                    {
                        _logger.LogInformation("Fetched {Count} shows from Sonarr instance '{Name}'",
                            shows.Count, instance.Name);

                        var showIds = new HashSet<int>();
                        foreach (var show in shows)
                        {
                            allShows.Add((show, instance.Id));
                            showIds.Add(show.Id);
                        }
                        instanceShowIds[instance.Id] = showIds;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to fetch shows from Sonarr instance '{Name}'", instance.Name);
                }
            }

            if (allShows.Count == 0)
            {
                _logger.LogWarning("No shows fetched from any Sonarr instance");
                await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
                return;
            }

            _logger.LogInformation("Fetched {Count} total shows from {InstanceCount} Sonarr instances", 
                allShows.Count, instances.Count);

            // Sync all shows with their instance IDs
            await _showSyncService.SyncShows(allShows);

            // Remove non-existent shows per instance
            var strategy = _dbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync();
                
                foreach (var instance in instances)
                {
                    if (instanceShowIds.TryGetValue(instance.Id, out var showIds))
                    {
                        await _showSyncService.RemoveNonExistentShows(showIds, instance.Id);
                    }
                }
                
                await transaction.CommitAsync();
            });

// Cleanup orphaned shows from deleted instances
            var configuredInstanceIds = instances.Select(i => i.Id).ToHashSet();

            // Include "default" and "legacy" to avoid removing migrated records
            configuredInstanceIds.Add("default");
            configuredInstanceIds.Add("legacy");

            var orphanedShows = await _dbContext.Shows
                .Where(s => !string.IsNullOrEmpty(s.SourceInstanceId) && 
                            !configuredInstanceIds.Contains(s.SourceInstanceId))
                .ToListAsync();

            if (orphanedShows.Count != 0)
            {
                _logger.LogWarning("Removing {Count} shows from deleted instances", orphanedShows.Count);
                _dbContext.Shows.RemoveRange(orphanedShows);
                await _dbContext.SaveChangesAsync();
            }

            // Cleanup orphaned translation requests for removed shows, seasons and episodes
            var existingShowIds = await _dbContext.Shows.Select(s => s.Id).ToListAsync();
            var existingSeasonIds = await _dbContext.Seasons.Select(s => s.Id).ToListAsync();
            var existingEpisodeIds = await _dbContext.Episodes.Select(e => e.Id).ToListAsync();
            var orphanedRequests = await _dbContext.TranslationRequests
                .Where(tr => tr.MediaId.HasValue &&
                             (tr.Status == TranslationStatus.Pending ||
                              tr.Status == TranslationStatus.InProgress ||
                              tr.Status == TranslationStatus.Failed) &&
                             ((tr.MediaType == MediaType.Show &&
                               !existingShowIds.Contains(tr.MediaId.Value)) ||
                              (tr.MediaType == MediaType.Season &&
                               !existingSeasonIds.Contains(tr.MediaId.Value)) ||
                              (tr.MediaType == MediaType.Episode &&
                               !existingEpisodeIds.Contains(tr.MediaId.Value))))
                .ToListAsync();

            if (orphanedRequests.Count != 0)
            {
                _logger.LogInformation("Removing {Count} orphaned translation requests for removed shows", 
                    orphanedRequests.Count);
                _dbContext.TranslationRequests.RemoveRange(orphanedRequests);
                await _dbContext.SaveChangesAsync();
            }

            await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
            _logger.LogInformation("Shows synced successfully from {Count} instances.", instances.Count);
        }
        catch (Exception ex)
        {
            await _scheduleService.UpdateJobState(jobName, JobStatus.Failed.GetDisplayName());
            _logger.LogError(ex,
                "An error occurred when syncing shows. Exception details: {ExceptionMessage}, Stack Trace: {StackTrace}",
                ex.Message, ex.StackTrace);
        }
    }

    /// <summary>
    /// Gets the list of Sonarr instances from settings.
    /// Falls back to single instance from old settings if multi-instance is not configured.
    /// </summary>
    private async Task<List<SonarrInstance>> GetSonarrInstances()
    {
        var instancesJson = await _settingService.GetSetting(SettingKeys.Integration.SonarrInstances);

        if (!string.IsNullOrEmpty(instancesJson))
        {
            try
            {
                var instances = JsonSerializer.Deserialize<List<SonarrInstance>>(instancesJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (instances != null && instances.Count > 0)
                {
                    // Filter out instances with missing required fields
                    var originalCount = instances.Count;
                    instances = instances
                        .Where(i => !string.IsNullOrWhiteSpace(i.Id) &&
                                    !string.IsNullOrWhiteSpace(i.Name) &&
                                    !string.IsNullOrWhiteSpace(i.Url) &&
                                    !string.IsNullOrWhiteSpace(i.ApiKey))
                        .ToList();
                    
                    if (instances.Count < originalCount)
                    {
                        _logger.LogWarning("Filtered out {Count} invalid Sonarr instances with missing required fields",
                            originalCount - instances.Count);
                    }
                    
                    if (instances.Count == 0)
                    {
                        _logger.LogWarning("No valid Sonarr instances after filtering");
                        return new List<SonarrInstance>();
                    }
                    
                    // Validate no duplicate IDs (would cause data corruption)
                    var duplicateIds = instances
                        .GroupBy(i => i.Id)
                        .Where(g => g.Count() > 1)
                        .Select(g => g.Key)
                        .ToList();
                        
                    if (duplicateIds.Count != 0)
                    {
                        _logger.LogError("Duplicate Sonarr instance IDs detected: {Ids}. Each instance must have a unique ID to prevent data corruption.", 
                            string.Join(", ", duplicateIds));
                        // Filter to first occurrence of each ID to prevent corruption
                        instances = instances
                            .GroupBy(i => i.Id)
                            .Select(g => g.First())
                            .ToList();
                        _logger.LogWarning("Filtered to unique instances: {Count} remaining", instances.Count);
                    }
                    
                    // Validate maximum instances
                    const int maxInstances = 10;
                    if (instances.Count > maxInstances)
                    {
                        _logger.LogWarning("Too many Sonarr instances configured ({Count}). Maximum is {Max}. Using first {Max} instances.",
                            instances.Count, maxInstances, maxInstances);
                        instances = instances.Take(maxInstances).ToList();
                    }
                    
                    return instances;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize Sonarr instances from settings");
            }
        }

        // Fall back to single instance from old settings
        var url = await _settingService.GetSetting(SettingKeys.Integration.SonarrUrl);
        var apiKey = await _settingService.GetSetting(SettingKeys.Integration.SonarrApiKey);

        if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(apiKey))
        {
            return new List<SonarrInstance>
            {
                new() { Id = "default", Name = "Default", Url = url, ApiKey = apiKey }
            };
        }

        return new List<SonarrInstance>();
    }
}
