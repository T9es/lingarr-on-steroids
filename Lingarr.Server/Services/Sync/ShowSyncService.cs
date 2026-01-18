using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services.Sync;
using Lingarr.Server.Models.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.Sync;

public class ShowSyncService : IShowSyncService
{
    private const int BatchSize = 25;
    
    private readonly LingarrDbContext _dbContext;
    private readonly IShowSync _showSync;
    private readonly ISeasonSync _seasonSync;
    private readonly IEpisodeSync _episodeSync;
    private readonly ILogger<ShowSyncService> _logger;

    public ShowSyncService(
        LingarrDbContext dbContext,
        IShowSync showSync,
        ISeasonSync seasonSync,
        IEpisodeSync episodeSync,
        ILogger<ShowSyncService> logger)
    {
        _dbContext = dbContext;
        _showSync = showSync;
        _seasonSync = seasonSync;
        _episodeSync = episodeSync;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SyncShows(List<SonarrShow> shows)
    {
        var processedCount = 0;
        
        // Process in batches to optimize database lookups and memory usage
        for (int i = 0; i < shows.Count; i += BatchSize)
        {
            var batch = shows.Skip(i).Take(BatchSize).ToList();
            var sonarrIds = batch.Select(s => s.Id).ToList();

            // Pre-load the entire hierarchy for the current batch
            List<Show> existingShows;
            try
            {
                _logger.LogInformation("Pre-loading batch of {Count} shows from database (Batch start: {Index})", batch.Count, i);
                existingShows = await _dbContext.Shows
                    .AsSplitQuery()
                    .Include(s => s.Images)
                    .Include(s => s.Seasons)
                        .ThenInclude(s => s.Episodes)
                            .ThenInclude(e => e.EmbeddedSubtitles)
                    .Where(s => sonarrIds.Contains(s.SonarrId))
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to pre-load batch of shows from database. Attempting to continue without pre-loading.");
                existingShows = new List<Show>();
            }

            var duplicates = existingShows.GroupBy(s => s.SonarrId).Where(g => g.Count() > 1).ToList();
            if (duplicates.Any())
            {
                foreach (var dup in duplicates)
                {
                    _logger.LogWarning("Duplicate SonarrId found in database: {SonarrId}. Count: {Count}", dup.Key, dup.Count());
                }
            }

            var showsBySonarrId = existingShows
                .GroupBy(s => s.SonarrId)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var sonarrShow in batch)
            {
                try
                {
                    if (sonarrShow.Title.Equals("Jujutsu Kaisen", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("DEBUG: Syncing Jujutsu Kaisen. SonarrId: {Id}, Path: {Path}, Seasons: {SeasonCount}",
                            sonarrShow.Id, sonarrShow.Path, sonarrShow.Seasons?.Count ?? 0);
                    }

                    showsBySonarrId.TryGetValue(sonarrShow.Id, out var showEntity);
                    showEntity = await _showSync.SyncShow(sonarrShow, showEntity);

                    if (sonarrShow.Seasons == null)
                    {
                        _logger.LogWarning("Show {Title} has no seasons in Sonarr response.", sonarrShow.Title);
                        processedCount++;
                        continue;
                    }

                    foreach (var sonarrSeason in sonarrShow.Seasons)
                    {
                        var existingSeason = showEntity.Seasons.FirstOrDefault(s => s.SeasonNumber == sonarrSeason.SeasonNumber);
                        var seasonEntity = await _seasonSync.SyncSeason(showEntity, sonarrShow, sonarrSeason, existingSeason);

                        await _episodeSync.SyncEpisodes(sonarrShow, seasonEntity, seasonEntity.Episodes.ToList());
                    }

                    processedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to sync show {Title} (SonarrId: {Id}). Skipping to next show.",
                        sonarrShow.Title, sonarrShow.Id);
                }
            }

            // Deferred saving: Save once per batch
            try
            {
                await SaveChanges(processedCount, shows.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save batch of shows to database. Changes for this batch may be lost.");
            }
            finally
            {
                _dbContext.ChangeTracker.Clear();
            }
        }
    }

    /// <inheritdoc />
    public async Task<Show> SyncShow(SonarrShow show)
    {
        // Pre-load hierarchy for single show
        var showEntity = await _dbContext.Shows
            .AsSplitQuery()
            .Include(s => s.Images)
            .Include(s => s.Seasons)
                .ThenInclude(s => s.Episodes)
                    .ThenInclude(e => e.EmbeddedSubtitles)
            .FirstOrDefaultAsync(s => s.SonarrId == show.Id);

        showEntity = await _showSync.SyncShow(show, showEntity);

        foreach (var season in show.Seasons)
        {
            var existingSeason = showEntity.Seasons.FirstOrDefault(s => s.SeasonNumber == season.SeasonNumber);
            var seasonEntity = await _seasonSync.SyncSeason(showEntity, show, season, existingSeason);
            await _episodeSync.SyncEpisodes(show, seasonEntity, seasonEntity.Episodes.ToList());
        }

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Synced a single show");

        return showEntity;
    }

    /// <inheritdoc />
    public async Task RemoveNonExistentShows(HashSet<int> existingSonarrIds)
    {
        var showsToDelete = await _dbContext.Shows
            .Include(s => s.Images)
            .Include(s => s.Seasons)
                .ThenInclude(s => s.Episodes)
            .Where(s => !existingSonarrIds.Contains(s.SonarrId))
            .ToListAsync();

        if (showsToDelete.Any())
        {
            _logger.LogInformation("Removing {Count} shows that no longer exist in Sonarr", showsToDelete.Count);

            var episodes = showsToDelete.SelectMany(s => s.Seasons.SelectMany(season => season.Episodes)).ToList();
            var seasons = showsToDelete.SelectMany(s => s.Seasons).ToList();
            var images = showsToDelete.SelectMany(s => s.Images).ToList();

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