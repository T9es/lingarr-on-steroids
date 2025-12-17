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

    public MovieSync(
        LingarrDbContext dbContext,
        PathConversionService pathConversionService,
        ILogger<MovieSync> logger,
        IImageSync imageSync,
        ISubtitleExtractionService extractionService,
        IMediaStateService mediaStateService)
    {
        _dbContext = dbContext;
        _pathConversionService = pathConversionService;
        _logger = logger;
        _imageSync = imageSync;
        _extractionService = extractionService;
        _mediaStateService = mediaStateService;
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
                DateAdded = DateTime.Parse(movie.Added),
                FileName = Path.GetFileNameWithoutExtension(moviePath),
                Path = Path.GetDirectoryName(moviePath) ?? string.Empty
            };
            _dbContext.Movies.Add(movieEntity);
        }
        else
        {
            movieEntity.Title = movie.Title;
            movieEntity.DateAdded = DateTime.Parse(movie.Added);
            movieEntity.FileName = Path.GetFileNameWithoutExtension(moviePath);
            movieEntity.Path = Path.GetDirectoryName(moviePath) ?? string.Empty;
        }

        _logger.LogInformation("Syncing movie: {MovieId} with Path: {Path}", movie.Id, movieEntity.Path);

        if (movie.Images?.Any() == true)
        {
            _imageSync.SyncImages(movieEntity.Images, movie.Images);
        }

        // Determine if we need to re-index embedded subtitles
        var fileChanged = !isNew && (
            oldPath != movieEntity.Path ||
            oldFileName != movieEntity.FileName);

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
        try
        {
            await _mediaStateService.UpdateStateAsync(movieEntity, MediaType.Movie);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update translation state for movie {Title}", movieEntity.Title);
        }

        return movieEntity;
    }
}
