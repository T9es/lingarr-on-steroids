using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services.Sync;
using Lingarr.Server.Models.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lingarr.Server.Services.Sync;

public class MovieSyncService : IMovieSyncService
{
    private const int BatchSize = 100;
    private readonly LingarrDbContext _dbContext;
    private readonly IMovieSync _movieSync;
    private readonly ILogger<MovieSyncService> _logger;

    public MovieSyncService(
        LingarrDbContext dbContext,
        IMovieSync movieSync,
        ILogger<MovieSyncService> logger)
    {
        _dbContext = dbContext;
        _movieSync = movieSync;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SyncMovies(List<(RadarrMovie Movie, string InstanceId)> movies)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            var processedCount = 0;
            IDbContextTransaction? currentTransaction = null;

            try
            {
                currentTransaction = await _dbContext.Database.BeginTransactionAsync();

                foreach (var (movie, instanceId) in movies)
                {
                    await _movieSync.SyncMovie(movie, instanceId);
                    processedCount++;

                    if (processedCount % BatchSize == 0)
                    {
                        await SaveChanges(processedCount, movies.Count);
                        await currentTransaction.CommitAsync();
                        await currentTransaction.DisposeAsync();
                        currentTransaction = await _dbContext.Database.BeginTransactionAsync();
                    }
                }

                if (processedCount % BatchSize != 0)
                {
                    await SaveChanges(processedCount, movies.Count);
                }

                await currentTransaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during movie sync. Rolling back transaction.");

                if (currentTransaction != null)
                {
                    await currentTransaction.RollbackAsync();
                }

                throw;
            }
            finally
            {
                if (currentTransaction != null)
                {
                    await currentTransaction.DisposeAsync();
                }
            }
        });
    }

    /// <inheritdoc />
    public async Task<Movie?> SyncMovie(RadarrMovie movie, string instanceId)
    {
        var movieEntity = await _movieSync.SyncMovie(movie, instanceId);
        await _dbContext.SaveChangesAsync();

        if (movieEntity != null)
        {
            _logger.LogInformation("Synced a single movie");
        }

        return movieEntity;
    }

    /// <inheritdoc />
    public async Task RemoveNonExistentMovies(IEnumerable<int> existingRadarrIds, string instanceId)
    {
        var missingMovies = await _dbContext.Movies
            .Include(movie => movie.EmbeddedSubtitles)
            .Where(movie => movie.SourceInstanceId == instanceId)
            .Where(movie => !existingRadarrIds.Contains(movie.RadarrId))
            .ToListAsync();

        if (missingMovies.Count == 0)
        {
            return;
        }

        // Radarr no longer reports these movies, so their indexed streams belong to files that
        // left the library. The metadata must not count as translation history here or the row
        // could never be hidden and pruned. Only database rows are removed, cached subtitle
        // files on disk are left alone.
        var staleEmbeddedSubtitles = missingMovies
            .SelectMany(movie => movie.EmbeddedSubtitles)
            .ToList();
        if (staleEmbeddedSubtitles.Count > 0)
        {
            _dbContext.EmbeddedSubtitles.RemoveRange(staleEmbeddedSubtitles);
            foreach (var movie in missingMovies)
            {
                movie.EmbeddedSubtitles.Clear();
            }

            // Persist the removal so the eligibility queries below no longer see the rows.
            await _dbContext.SaveChangesAsync();
        }

        var utcNow = DateTime.UtcNow;
        var newlyMissing = 0;
        var keptVisible = 0;

        foreach (var movie in missingMovies)
        {
            var isPruneEligible = await MissingMediaPrunePolicy.IsEligibleForMissingTrackingAsync(
                _dbContext,
                movie);
            if (isPruneEligible)
            {
                if (movie.MissingSince == null)
                {
                    movie.MissingSince = utcNow;
                    newlyMissing++;
                }

                continue;
            }

            if (movie.MissingSince != null)
            {
                // The row gained history or per-item settings, so it must stay visible.
                movie.MissingSince = null;
                keptVisible++;
            }
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Marked {NewlyMissing} of {Count} movies missing from Radarr instance '{InstanceId}' for later pruning; {KeptVisible} kept visible because they carry history or per-item settings; removed {StaleEmbeddedCount} stale embedded subtitle row(s).",
            newlyMissing,
            missingMovies.Count,
            instanceId,
            keptVisible,
            staleEmbeddedSubtitles.Count);
    }

    /// <summary>
    /// Saves pending changes to the database and logs the sync progress
    /// </summary>
    /// <param name="processedCount">The number of movies processed so far</param>
    /// <param name="totalCount">The total number of movies to process</param>
    /// <returns>A task representing the asynchronous operation</returns>
    private async Task SaveChanges(int processedCount, int totalCount)
    {
        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Synced and saved {ProcessedCount} out of {TotalCount} movies", 
            processedCount, totalCount);
    }
}
