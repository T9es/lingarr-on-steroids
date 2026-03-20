using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
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

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();
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

                result.ReassignedInstanceIds = allNonDefaultInstances;

                _logger.LogInformation("Found {Count} non-default instance IDs: {Instances}",
                    allNonDefaultInstances.Count, string.Join(", ", allNonDefaultInstances));

                // Step 2: Handle movies with non-default instance IDs
                // Must delete collisions first (same RadarrId already exists at 'default'),
                // then reassign the remaining non-colliding records
                if (nonDefaultMovieInstances.Any())
                {
                    // Find RadarrIds that already exist at 'default' instance
                    var defaultRadarrIds = await _dbContext.Movies
                        .Where(m => m.SourceInstanceId == "default")
                        .Select(m => m.RadarrId)
                        .ToListAsync();
                    var defaultRadarrIdSet = new HashSet<int>(defaultRadarrIds);

                    // Delete non-default movies that would collide (RadarrId already at 'default')
                    var collidingMovies = await _dbContext.Movies
                        .Where(m => m.SourceInstanceId != null && m.SourceInstanceId != "default"
                                    && defaultRadarrIdSet.Contains(m.RadarrId))
                        .ToListAsync();

                    if (collidingMovies.Count > 0)
                    {
                        _dbContext.Movies.RemoveRange(collidingMovies);
                        await _dbContext.SaveChangesAsync();
                        result.DuplicatesRemoved += collidingMovies.Count;
                        _logger.LogInformation("Deleted {Count} colliding non-default movies (RadarrId already exists at 'default')", collidingMovies.Count);
                    }

                    // Reassign remaining non-default movies to 'default' (safe, no collisions)
                    var moviesReassigned = await _dbContext.Movies
                        .Where(m => m.SourceInstanceId != null && m.SourceInstanceId != "default")
                        .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.SourceInstanceId, "default"));

                    result.MoviesReassigned = moviesReassigned;
                    _logger.LogInformation("Reassigned {Count} movies to 'default' instance", moviesReassigned);
                }

                // Step 3: Handle shows with non-default instance IDs (same approach)
                if (nonDefaultShowInstances.Any())
                {
                    // Find SonarrIds that already exist at 'default' instance
                    var defaultSonarrIds = await _dbContext.Shows
                        .Where(s => s.SourceInstanceId == "default")
                        .Select(s => s.SonarrId)
                        .ToListAsync();
                    var defaultSonarrIdSet = new HashSet<int>(defaultSonarrIds);

                    // Delete non-default shows that would collide (SonarrId already at 'default')
                    var collidingShows = await _dbContext.Shows
                        .Where(s => s.SourceInstanceId != null && s.SourceInstanceId != "default"
                                    && defaultSonarrIdSet.Contains(s.SonarrId))
                        .ToListAsync();

                    if (collidingShows.Count > 0)
                    {
                        _dbContext.Shows.RemoveRange(collidingShows);
                        await _dbContext.SaveChangesAsync();
                        result.DuplicatesRemoved += collidingShows.Count;
                        _logger.LogInformation("Deleted {Count} colliding non-default shows (SonarrId already exists at 'default')", collidingShows.Count);
                    }

                    // Reassign remaining non-default shows to 'default' (safe, no collisions)
                    var showsReassigned = await _dbContext.Shows
                        .Where(s => s.SourceInstanceId != null && s.SourceInstanceId != "default")
                        .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.SourceInstanceId, "default"));

                    result.ShowsReassigned = showsReassigned;
                    _logger.LogInformation("Reassigned {Count} shows to 'default' instance", showsReassigned);
                }

                // Step 4: Clean up true duplicates (same RadarrId/SonarrId + same SourceInstanceId 'default')
                // This runs AFTER reassignment so any collisions get resolved
                var duplicateMoviesDeleted = await CleanupTrueDuplicates(isMovie: true);
                var duplicateShowsDeleted = await CleanupTrueDuplicates(isMovie: false);
                result.DuplicatesRemoved = duplicateMoviesDeleted + duplicateShowsDeleted;

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

                result.Message = $"Cleanup complete. Reassigned {result.MoviesReassigned} movies and {result.ShowsReassigned} shows to 'default'. " +
                               $"Removed {result.DuplicatesRemoved} true duplicates. " +
                               $"Consolidated {result.InstancesConsolidated} instances to 'default'.";

                _logger.LogInformation(result.Message);

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during duplicate instance cleanup, rolling back");
                await transaction.RollbackAsync();
                result.Success = false;
                result.Message = $"Error during cleanup: {ex.Message}";
            }
        });

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
                var radarrInstances = System.Text.Json.JsonSerializer.Deserialize<List<InstanceSetting>>(radarrInstancesStr);
                if (radarrInstances != null && radarrInstances.Count > 0)
                {
                    // Check if any instance has a non-default ID (single or multiple)
                    var hasNonDefault = radarrInstances.Any(i => i.Id != "default");
                    if (radarrInstances.Count > 1 || hasNonDefault)
                    {
                        var defaultInstance = radarrInstances.FirstOrDefault(i => i.Id == "default") 
                            ?? radarrInstances.First();
                        
                        defaultInstance.Id = "default";
                        
                        var consolidatedJson = System.Text.Json.JsonSerializer.Serialize(new[] { defaultInstance });
                        await _settingService.SetSetting(SettingKeys.Integration.RadarrInstances, consolidatedJson);
                        result.InstancesConsolidated += radarrInstances.Count > 1 ? radarrInstances.Count - 1 : 0;
                        _logger.LogInformation("Consolidated Radarr instances to single 'default' instance (was {Count} instances)", radarrInstances.Count);
                    }
                }
            }

            // Parse and consolidate Sonarr instances
            if (!string.IsNullOrEmpty(sonarrInstancesStr))
            {
                var sonarrInstances = System.Text.Json.JsonSerializer.Deserialize<List<InstanceSetting>>(sonarrInstancesStr);
                if (sonarrInstances != null && sonarrInstances.Count > 0)
                {
                    // Check if any instance has a non-default ID (single or multiple)
                    var hasNonDefault = sonarrInstances.Any(i => i.Id != "default");
                    if (sonarrInstances.Count > 1 || hasNonDefault)
                    {
                        var defaultInstance = sonarrInstances.FirstOrDefault(i => i.Id == "default") 
                            ?? sonarrInstances.First();
                        
                        defaultInstance.Id = "default";
                        
                        var consolidatedJson = System.Text.Json.JsonSerializer.Serialize(new[] { defaultInstance });
                        await _settingService.SetSetting(SettingKeys.Integration.SonarrInstances, consolidatedJson);
                        result.InstancesConsolidated += sonarrInstances.Count > 1 ? sonarrInstances.Count - 1 : 0;
                        _logger.LogInformation("Consolidated Sonarr instances to single 'default' instance (was {Count} instances)", sonarrInstances.Count);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not update instance settings, but database cleanup was successful");
        }
    }
}
