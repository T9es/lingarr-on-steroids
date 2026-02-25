using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

/// <summary>
/// Service for cleaning up duplicate records and consolidating instances
/// </summary>
public class CleanupService : ICleanupService
{
    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settingService;
    private readonly ILogger<CleanupService> _logger;

    public CleanupService(
        LingarrDbContext dbContext,
        ISettingService settingService,
        ILogger<CleanupService> logger)
    {
        _dbContext = dbContext;
        _settingService = settingService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CleanupResult> CleanupDuplicateInstances()
    {
        var result = new CleanupResult { Success = true };

        try
        {
            // Step 1: Find all non-default instance IDs
            var nonDefaultMovieInstances = await _dbContext.Movies
                .Where(m => m.SourceInstanceId != null && m.SourceInstanceId != "default")
                .Select(m => m.SourceInstanceId!)
                .Distinct()
                .ToListAsync();

            var nonDefaultShowInstances = await _dbContext.Shows
                .Where(s => s.SourceInstanceId != null && s.SourceInstanceId != "default")
                .Select(s => s.SourceInstanceId!)
                .Distinct()
                .ToListAsync();

            var allNonDefaultInstances = nonDefaultMovieInstances
                .Union(nonDefaultShowInstances)
                .ToList();

            result.RemovedInstanceIds = allNonDefaultInstances;

            _logger.LogInformation("Found {Count} non-default instance IDs: {Instances}",
                allNonDefaultInstances.Count, string.Join(", ", allNonDefaultInstances));

            // Step 2: Delete movies with non-default instance IDs
            if (nonDefaultMovieInstances.Any())
            {
                var moviesToDelete = await _dbContext.Movies
                    .Where(m => m.SourceInstanceId != null && m.SourceInstanceId != "default")
                    .ToListAsync();

                _dbContext.Movies.RemoveRange(moviesToDelete);
                result.MoviesDeleted = moviesToDelete.Count;
                _logger.LogInformation("Marked {Count} movies for deletion (non-default instances)", moviesToDelete.Count);
            }

            // Step 3: Delete shows with non-default instance IDs
            if (nonDefaultShowInstances.Any())
            {
                var showsToDelete = await _dbContext.Shows
                    .Where(s => s.SourceInstanceId != null && s.SourceInstanceId != "default")
                    .ToListAsync();

                _dbContext.Shows.RemoveRange(showsToDelete);
                result.ShowsDeleted = showsToDelete.Count;
                _logger.LogInformation("Marked {Count} shows for deletion (non-default instances)", showsToDelete.Count);
            }

            // Step 4: Clean up true duplicates (same RadarrId/SonarrId + same SourceInstanceId)
            var duplicateMoviesDeleted = await CleanupTrueDuplicates(isMovie: true);
            var duplicateShowsDeleted = await CleanupTrueDuplicates(isMovie: false);
            result.MoviesDeleted += duplicateMoviesDeleted;
            result.ShowsDeleted += duplicateShowsDeleted;

            // Step 5: Set NULL instance IDs to 'default'
            var nullMoviesUpdated = await _dbContext.Movies
                .Where(m => m.SourceInstanceId == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.SourceInstanceId, "default"));

            var nullShowsUpdated = await _dbContext.Shows
                .Where(s => s.SourceInstanceId == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.SourceInstanceId, "default"));

            _logger.LogInformation("Updated {Movies} movies and {Shows} shows with NULL instance ID to 'default'",
                nullMoviesUpdated, nullShowsUpdated);

            // Step 6: Save all changes
            await _dbContext.SaveChangesAsync();

            // Step 7: Update settings to have only 'default' instance
            await UpdateInstanceSettings(result);

            result.Message = $"Cleanup complete. Deleted {result.MoviesDeleted} movies and {result.ShowsDeleted} shows. " +
                           $"Consolidated {result.InstancesConsolidated} instances to 'default'.";

            _logger.LogInformation(result.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during duplicate instance cleanup");
            result.Success = false;
            result.Message = $"Error during cleanup: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Cleans up true duplicates (same ID + same instance)
    /// </summary>
    private async Task<int> CleanupTrueDuplicates(bool isMovie)
    {
        var deletedCount = 0;

        if (isMovie)
        {
            var duplicates = await _dbContext.Movies
                .GroupBy(m => new { m.RadarrId, m.SourceInstanceId })
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToListAsync();

            foreach (var key in duplicates)
            {
                var toDelete = await _dbContext.Movies
                    .Where(m => m.RadarrId == key.RadarrId && m.SourceInstanceId == key.SourceInstanceId)
                    .OrderByDescending(m => m.Id) // Keep oldest (lowest ID)
                    .Skip(1)
                    .ToListAsync();

                _dbContext.Movies.RemoveRange(toDelete);
                deletedCount += toDelete.Count;
            }
        }
        else
        {
            var duplicates = await _dbContext.Shows
                .GroupBy(s => new { s.SonarrId, s.SourceInstanceId })
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToListAsync();

            foreach (var key in duplicates)
            {
                var toDelete = await _dbContext.Shows
                    .Where(s => s.SonarrId == key.SonarrId && s.SourceInstanceId == key.SourceInstanceId)
                    .OrderByDescending(s => s.Id) // Keep oldest (lowest ID)
                    .Skip(1)
                    .ToListAsync();

                _dbContext.Shows.RemoveRange(toDelete);
                deletedCount += toDelete.Count;
            }
        }

        return deletedCount;
    }

    /// <summary>
    /// Updates the instance settings to have only 'default' instance
    /// </summary>
    private async Task UpdateInstanceSettings(CleanupResult result)
    {
        try
        {
            // Get current instance settings
            var radarrInstancesStr = await _settingService.GetSetting(SettingKeys.Integration.RadarrInstances);
            var sonarrInstancesStr = await _settingService.GetSetting(SettingKeys.Integration.SonarrInstances);

            // Parse and consolidate Radarr instances
            if (!string.IsNullOrEmpty(radarrInstancesStr))
            {
                var radarrInstances = System.Text.Json.JsonSerializer.Deserialize<List<InstanceConfig>>(radarrInstancesStr);
                if (radarrInstances != null && radarrInstances.Count > 1)
                {
                    // Keep only the 'default' instance, or the first one if no 'default' exists
                    var defaultInstance = radarrInstances.FirstOrDefault(i => i.Id == "default") 
                        ?? radarrInstances.First();
                    
                    defaultInstance.Id = "default"; // Ensure ID is 'default'
                    
                    var consolidatedJson = System.Text.Json.JsonSerializer.Serialize(new[] { defaultInstance });
                    await _settingService.SetSetting(SettingKeys.Integration.RadarrInstances, consolidatedJson);
                    result.InstancesConsolidated += radarrInstances.Count - 1;
                    _logger.LogInformation("Consolidated {Count} Radarr instances to single 'default' instance", radarrInstances.Count);
                }
            }

            // Parse and consolidate Sonarr instances
            if (!string.IsNullOrEmpty(sonarrInstancesStr))
            {
                var sonarrInstances = System.Text.Json.JsonSerializer.Deserialize<List<InstanceConfig>>(sonarrInstancesStr);
                if (sonarrInstances != null && sonarrInstances.Count > 1)
                {
                    // Keep only the 'default' instance, or the first one if no 'default' exists
                    var defaultInstance = sonarrInstances.FirstOrDefault(i => i.Id == "default") 
                        ?? sonarrInstances.First();
                    
                    defaultInstance.Id = "default"; // Ensure ID is 'default'
                    
                    var consolidatedJson = System.Text.Json.JsonSerializer.Serialize(new[] { defaultInstance });
                    await _settingService.SetSetting(SettingKeys.Integration.SonarrInstances, consolidatedJson);
                    result.InstancesConsolidated += sonarrInstances.Count - 1;
                    _logger.LogInformation("Consolidated {Count} Sonarr instances to single 'default' instance", sonarrInstances.Count);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not update instance settings, but database cleanup was successful");
        }
    }

    /// <summary>
    /// Helper class for deserializing instance config
    /// </summary>
    private class InstanceConfig
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
    }
}
