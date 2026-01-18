using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Sync;
using Lingarr.Server.Models.Integrations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lingarr.Server.Services.Sync;

public class MovieSync : IMovieSync
{
    private readonly LingarrDbContext _dbContext;
    private readonly PathConversionService _pathConversionService;
    private readonly ILogger<MovieSync> _logger;
    private readonly IImageSync _imageSync;
    private readonly ISubtitleExtractionService _extractionService;
    private readonly IMediaStateService _mediaStateService;
    private readonly IOrphanSubtitleCleanupService _orphanCleanupService;

    public MovieSync(
        LingarrDbContext dbContext,
        PathConversionService pathConversionService,
        ILogger<MovieSync> logger,
        IImageSync imageSync,
        ISubtitleExtractionService extractionService,
        IMediaStateService mediaStateService,
        IOrphanSubtitleCleanupService orphanCleanupService)
    {
        _dbContext = dbContext;
        _pathConversionService = pathConversionService;
        _logger = logger;
        _imageSync = imageSync;
        _extractionService = extractionService;
        _mediaStateService = mediaStateService;
        _orphanCleanupService = orphanCleanupService;
    }

    /// <inheritdoc />
    public async Task<Movie?> SyncMovie(RadarrMovie movie)
    {
        if (!movie.HasFile)
        {
            _logger.LogDebug("Movie '{Title}' (ID: {Id}) has no file, skipping.", movie.Title, movie.Id);
            return null;
        }

        var movieEntity = await _dbContext.Movies
            .AsSplitQuery()
            .Include(m => m.Images)
            .Include(m => m.EmbeddedSubtitles)
            .FirstOrDefaultAsync(m => m.RadarrId == movie.Id);

        var moviePath = _pathConversionService.ConvertAndMapPath(
            movie.MovieFile.Path ?? string.Empty,
            MediaType.Movie
        );

        var isNew = movieEntity == null;
        var oldPath = movieEntity?.Path;
        var oldFileName = movieEntity?.FileName;

        if (movieEntity == null)
        {
            movieEntity = new Movie
            {
                RadarrId = movie.Id,
                Title = movie.Title,
                DateAdded = DateTime.Parse(movie.Added).ToUniversalTime(),
                FileName = Path.GetFileNameWithoutExtension(moviePath),
                Path = Path.GetDirectoryName(moviePath) ?? string.Empty
            };
            _dbContext.Movies.Add(movieEntity);
        }
        else
        {
            movieEntity.Title = movie.Title;
            movieEntity.DateAdded = DateTime.Parse(movie.Added).ToUniversalTime();
            movieEntity.FileName = Path.GetFileNameWithoutExtension(moviePath);
            movieEntity.Path = Path.GetDirectoryName(moviePath) ?? string.Empty;
        }

        _logger.LogInformation("Syncing movie: {MovieId} with Path: {Path}", movie.Id, movieEntity.Path);

        if (movie.Images?.Any() == true)
        {
            _imageSync.SyncImages(movieEntity.Images, movie.Images);
        }

        // Determine if we need to re-index embedded subtitles
        // Safe detection: only trigger if the filename actually changes (media upgraded)
        var fileChanged = !isNew && (oldFileName != movieEntity.FileName);

        if (!isNew && !fileChanged && !string.IsNullOrEmpty(movieEntity.Path) && !string.IsNullOrEmpty(movieEntity.FileName))
        {
            // If path changed but filename is the same, it's just a move. 
            // We update the path but don't need to re-index or clean orphans unless we're paranoid.
            // But if mtime changed on the same file, we might need re-indexing.
            
            try 
            {
                var dirInfo = new DirectoryInfo(movieEntity.Path);
                if (dirInfo.Exists)
                {
                    var fileInfo = dirInfo.GetFiles(movieEntity.FileName + ".*")
                        .FirstOrDefault(f => !SubtitleExtensions.Contains(f.Extension.ToLowerInvariant()));
                    
                    if (fileInfo != null)
                    {
                        if (movieEntity.IndexedAt.HasValue && fileInfo.LastWriteTimeUtc > movieEntity.IndexedAt.Value.AddSeconds(5))
                        {
                            _logger.LogInformation("Movie file {Title} appears to have been refreshed (mtime changed), triggering re-index", movieEntity.Title);
                            fileChanged = true;

                            // Clean up stale translated subtitles when media is refreshed
                            if (!string.IsNullOrEmpty(movieEntity.Path) && !string.IsNullOrEmpty(movieEntity.FileName))
                            {
                                await _orphanCleanupService.CleanupStaleSubtitlesAsync(
                                    movieEntity.Path,
                                    movieEntity.FileName);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to check mtime for movie {Title}", movieEntity.Title);
            }
        }

        // Clean up orphaned subtitles when the filename changes (e.g., media upgraded)
        if (fileChanged && !string.IsNullOrEmpty(oldPath) && !string.IsNullOrEmpty(oldFileName))
        {
            await _orphanCleanupService.CleanupOrphansAsync(
                oldPath,
                oldFileName,
                movieEntity.FileName!);
        }

        var needsIndexing = isNew || fileChanged || movieEntity.IndexedAt == null;

        if (needsIndexing)
        {
            try
            {
                // Save first so the entity has an ID for the extraction service
                await _dbContext.SaveChangesAsync();
                
                await _extractionService.SyncEmbeddedSubtitles(movieEntity);
                movieEntity.IndexedAt = DateTime.UtcNow;
                
                _logger.LogDebug("Indexed embedded subtitles for movie {Title}", movieEntity.Title);
            }
            catch (Exception ex)
            {
                // Check if this is a deadlock/serialization failure that we should let bubble up
                // PostgreSQL: 40001 = serialization_failure, 40P01 = deadlock_detected
                if (ex is DbUpdateException dbEx && dbEx.InnerException is PostgresException pgEx && 
                    (pgEx.SqlState == "40001" || pgEx.SqlState == "40P01"))
                {
                    _logger.LogWarning("Deadlock/serialization failure detected during embedded subtitle sync for movie {Title}. Rethrowing to utilize execution strategy.", movieEntity.Title);
                    throw;
                }

                _logger.LogWarning(ex, "Failed to index embedded subtitles for movie {Title}", movieEntity.Title);
            }
        }

        // Update translation state
        // For AwaitingSource: only re-check if directory mtime changed (reduces I/O)
        try
        {
            var shouldUpdateState = true;
            
            if (movieEntity.TranslationState == TranslationState.AwaitingSource && 
                !string.IsNullOrEmpty(movieEntity.Path))
            {
                var dirInfo = new DirectoryInfo(movieEntity.Path);
                if (dirInfo.Exists)
                {
                    var dirMtime = dirInfo.LastWriteTimeUtc;
                    if (movieEntity.LastSubtitleCheckAt.HasValue && 
                        dirMtime <= movieEntity.LastSubtitleCheckAt.Value)
                    {
                        // Directory unchanged, skip expensive filesystem scan
                        shouldUpdateState = false;
                        _logger.LogDebug("Skipping subtitle check for {Title}: directory unchanged", movieEntity.Title);
                    }
                }
            }
            
            if (shouldUpdateState)
            {
                await _mediaStateService.UpdateStateAsync(movieEntity, MediaType.Movie);
                movieEntity.LastSubtitleCheckAt = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update translation state for movie {Title}", movieEntity.Title);
        }

        return movieEntity;
    }

    private static readonly string[] SubtitleExtensions = { ".srt", ".ass", ".ssa", ".sub" };
}
