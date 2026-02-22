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

public class SyncMovieJob
{
    private readonly IRadarrService _radarrService;
    private readonly ILogger<SyncMovieJob> _logger;
    private readonly IScheduleService _scheduleService;
    private readonly IMovieSyncService _movieSyncService;
    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settingService;

    public SyncMovieJob(
        IRadarrService radarrService,
        ILogger<SyncMovieJob> logger,
        IScheduleService scheduleService,
        IMovieSyncService movieSyncService,
        LingarrDbContext dbContext,
        ISettingService settingService)
    {
        _radarrService = radarrService;
        _logger = logger;
        _scheduleService = scheduleService;
        _movieSyncService = movieSyncService;
        _dbContext = dbContext;
        _settingService = settingService;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    [AutomaticRetry(Attempts = 0)]
    [Queue("movies")]
    public async Task Execute()
    {
        var jobName = JobContextFilter.GetCurrentJobTypeName();
        _logger.LogInformation("Radarr sync job initiated");

        try
        {
            await _scheduleService.UpdateJobState(jobName, JobStatus.Processing.GetDisplayName());

            // Get instances from settings
            var instances = await GetRadarrInstances();
            if (instances.Count == 0)
            {
                _logger.LogWarning("No Radarr instances configured");
                await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
                return;
            }

            var allMovies = new List<(RadarrMovie Movie, string InstanceId)>();
            var instanceMovieIds = new Dictionary<string, HashSet<int>>();

            foreach (var instance in instances)
            {
                try
                {
                    var movies = await _radarrService.GetMovies(instance.Url, instance.ApiKey);
                    if (movies != null)
                    {
                        _logger.LogInformation("Fetched {Count} movies from Radarr instance '{Name}'",
                            movies.Count, instance.Name);

                        var movieIds = new HashSet<int>();
                        foreach (var movie in movies)
                        {
                            allMovies.Add((movie, instance.Id));
                            movieIds.Add(movie.Id);
                        }
                        instanceMovieIds[instance.Id] = movieIds;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to fetch movies from Radarr instance '{Name}'", instance.Name);
                }
            }

            if (allMovies.Count == 0)
            {
                _logger.LogWarning("No movies fetched from any Radarr instance");
                await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
                return;
            }

            // Sync all movies with their instance IDs
            await _movieSyncService.SyncMovies(allMovies);

            // Remove non-existent movies per instance
            var strategy = _dbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync();
                
                foreach (var instance in instances)
                {
                    if (instanceMovieIds.TryGetValue(instance.Id, out var movieIds))
                    {
                        await _movieSyncService.RemoveNonExistentMovies(movieIds, instance.Id);
                    }
                }
                
                await transaction.CommitAsync();
            });

            await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
            _logger.LogInformation("Movies synced successfully from {Count} instances.", instances.Count);
        }
        catch (Exception ex)
        {
            await _scheduleService.UpdateJobState(jobName, JobStatus.Failed.GetDisplayName());
            _logger.LogError(ex,
                "An error occurred when syncing movies. Exception details: {ExceptionMessage}, Stack Trace: {StackTrace}",
                ex.Message, ex.StackTrace);
        }
    }

    /// <summary>
    /// Gets the list of Radarr instances from settings.
    /// Falls back to single instance from old settings if multi-instance is not configured.
    /// </summary>
    private async Task<List<RadarrInstance>> GetRadarrInstances()
    {
        var instancesJson = await _settingService.GetSetting(SettingKeys.Integration.RadarrInstances);

        if (!string.IsNullOrEmpty(instancesJson))
        {
            try
            {
                var instances = JsonSerializer.Deserialize<List<RadarrInstance>>(instancesJson);
                if (instances != null && instances.Count > 0)
                {
                    // Validate no duplicate IDs (would cause data corruption)
                    var duplicateIds = instances
                        .GroupBy(i => i.Id)
                        .Where(g => g.Count() > 1)
                        .Select(g => g.Key)
                        .ToList();
                        
                    if (duplicateIds.Count != 0)
                    {
                        _logger.LogError("Duplicate Radarr instance IDs detected: {Ids}. Each instance must have a unique ID to prevent data corruption.", 
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
                        _logger.LogWarning("Too many Radarr instances configured ({Count}). Maximum is {Max}. Using first {Max} instances.",
                            instances.Count, maxInstances, maxInstances);
                        instances = instances.Take(maxInstances).ToList();
                    }
                    
                    return instances;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize Radarr instances from settings");
            }
        }

        // Fall back to single instance from old settings
        var url = await _settingService.GetSetting(SettingKeys.Integration.RadarrUrl);
        var apiKey = await _settingService.GetSetting(SettingKeys.Integration.RadarrApiKey);

        if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(apiKey))
        {
            return new List<RadarrInstance>
            {
                new() { Id = "default", Name = "Default", Url = url, ApiKey = apiKey }
            };
        }

        return new List<RadarrInstance>();
    }
}
