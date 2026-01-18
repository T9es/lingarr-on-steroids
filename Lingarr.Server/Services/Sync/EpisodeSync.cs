using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Integration;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Sync;
using Lingarr.Server.Models.Integrations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lingarr.Server.Services.Sync;

public class EpisodeSync : IEpisodeSync
{
    private readonly ISonarrService _sonarrService;
    private readonly PathConversionService _pathConversionService;
    private readonly ISubtitleExtractionService _extractionService;
    private readonly IMediaStateService _mediaStateService;
    private readonly IOrphanSubtitleCleanupService _orphanCleanupService;
    private readonly ILogger<EpisodeSync> _logger;

    private readonly LingarrDbContext _dbContext;

    public EpisodeSync(
        ISonarrService sonarrService,
        PathConversionService pathConversionService,
        ISubtitleExtractionService extractionService,
        IMediaStateService mediaStateService,
        IOrphanSubtitleCleanupService orphanCleanupService,
        ILogger<EpisodeSync> logger,
        LingarrDbContext dbContext)
    {
        _sonarrService = sonarrService;
        _pathConversionService = pathConversionService;
        _extractionService = extractionService;
        _mediaStateService = mediaStateService;
        _orphanCleanupService = orphanCleanupService;
        _logger = logger;
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task SyncEpisodes(SonarrShow show, Season season)
    {
        var episodes = await _sonarrService.GetEpisodes(show.Id, season.SeasonNumber);
        if (episodes == null) return;

        var syncedEpisodes = new List<(Episode Entity, bool NeedsIndexing, string? OldPath, string? OldFileName)>();

        foreach (var episode in episodes.Where(e => e.HasFile))
        {
            var episodePathResult = await _sonarrService.GetEpisodePath(episode.Id);
            var episodePath = _pathConversionService.ConvertAndMapPath(
                episodePathResult?.EpisodeFile.Path ?? string.Empty,
                MediaType.Show
            );
            
            var (entity, needsIndexing, oldPath, oldFileName) = await UpdateEpisodeMetadata(episode, episodePath, season, episodePathResult?.EpisodeFile.DateAdded);
            syncedEpisodes.Add((entity, needsIndexing, oldPath, oldFileName));
        }

        // Batch save all metadata updates/additions
        if (_dbContext.ChangeTracker.HasChanges())
        {
            await _dbContext.SaveChangesAsync();
        }

        // Now that IDs are assigned for new entities, perform indexing and state updates
        foreach (var (entity, needsIndexing, oldPath, oldFileName) in syncedEpisodes)
        {
            // Clean up orphaned subtitles when the filename actually changes (media upgraded)
            if (!string.IsNullOrEmpty(oldPath) && !string.IsNullOrEmpty(oldFileName) && oldFileName != entity.FileName)
            {
                await _orphanCleanupService.CleanupOrphansAsync(
                    oldPath,
                    oldFileName,
                    entity.FileName!);
            }

            if (needsIndexing)
            {
                // We perform individual indexing for now because SyncEmbeddedSubtitles is complex and hits filesystem
                await IndexEmbeddedSubtitles(entity, saveChanges: false);
            }

            // Update state - for AwaitingSource, check mtime first (reduces I/O)
            try
            {
                var shouldUpdateState = true;
                
                if (entity.TranslationState == TranslationState.AwaitingSource && 
                    !string.IsNullOrEmpty(entity.Path))
                {
                    var dirInfo = new DirectoryInfo(entity.Path);
                    if (dirInfo.Exists)
                    {
                        var dirMtime = dirInfo.LastWriteTimeUtc;
                        if (entity.LastSubtitleCheckAt.HasValue && 
                            dirMtime <= entity.LastSubtitleCheckAt.Value)
                        {
                            shouldUpdateState = false;
                            _logger.LogDebug("Skipping subtitle check for {Title}: directory unchanged", entity.Title);
                        }
                    }
                }
                
                if (shouldUpdateState)
                {
                    await _mediaStateService.UpdateStateAsync(entity, MediaType.Episode, saveChanges: false);
                    entity.LastSubtitleCheckAt = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update translation state for episode {Title}", entity.Title);
            }
        }

        // Final batch save for indexing and state updates
        if (_dbContext.ChangeTracker.HasChanges())
        {
            await _dbContext.SaveChangesAsync();
        }

        RemoveNonExistentEpisodes(season, episodes);
    }

    /// <summary>
    /// Updates or creates the episode entity metadata without saving to DB.
    /// Returns the entity, whether it needs indexing, and old path/filename if changed.
    /// </summary>
    private async Task<(Episode Entity, bool NeedsIndexing, string? OldPath, string? OldFileName)> UpdateEpisodeMetadata(SonarrEpisode episode, string episodePath, Season season, DateTime? dateAdded)
    {
        var episodeEntity = season.Episodes.FirstOrDefault(se => se.SonarrId == episode.Id);
        
        var isNew = episodeEntity == null;
        var oldPath = episodeEntity?.Path;
        var oldFileName = episodeEntity?.FileName;
        
        if (episodeEntity == null)
        {
            episodeEntity = new Episode
            {
                SonarrId = episode.Id,
                EpisodeNumber = episode.EpisodeNumber,
                Title = episode.Title,
                FileName = Path.GetFileNameWithoutExtension(episodePath),
                Path = Path.GetDirectoryName(episodePath),
                Season = season,
                DateAdded = dateAdded?.ToUniversalTime()
            };
            season.Episodes.Add(episodeEntity);
        }
        else
        {
            episodeEntity.EpisodeNumber = episode.EpisodeNumber;
            episodeEntity.Title = episode.Title;
            episodeEntity.FileName = Path.GetFileNameWithoutExtension(episodePath);
            episodeEntity.Path = Path.GetDirectoryName(episodePath);
            episodeEntity.DateAdded = dateAdded?.ToUniversalTime();
        }

        var fileChanged = !isNew && (
            oldPath != episodeEntity.Path ||
            oldFileName != episodeEntity.FileName);

        if (!isNew && !fileChanged && !string.IsNullOrEmpty(episodeEntity.Path) && !string.IsNullOrEmpty(episodeEntity.FileName))
        {
            try 
            {
                var dirInfo = new DirectoryInfo(episodeEntity.Path);
                if (dirInfo.Exists)
                {
                    var fileInfo = dirInfo.GetFiles(episodeEntity.FileName + ".*")
                        .FirstOrDefault(f => !SubtitleExtensions.Contains(f.Extension.ToLowerInvariant()));
                    
                    if (fileInfo != null)
                    {
                        if (episodeEntity.IndexedAt.HasValue && fileInfo.LastWriteTimeUtc > episodeEntity.IndexedAt.Value.AddSeconds(5))
                        {
                            _logger.LogInformation("Episode file {Title} appears to have been refreshed (mtime changed), triggering re-index", episodeEntity.Title);
                            fileChanged = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to check mtime for episode {Title}", episodeEntity.Title);
            }
        }

        var needsIndexing = isNew || fileChanged || episodeEntity.IndexedAt == null;
        
        // Return old values only if file actually changed
        return (episodeEntity, needsIndexing, fileChanged ? oldPath : null, fileChanged ? oldFileName : null);
    }

    private static readonly string[] SubtitleExtensions = { ".srt", ".ass", ".ssa", ".sub" };

    private async Task IndexEmbeddedSubtitles(Episode episodeEntity, bool saveChanges = true)
    {
        try
        {
            await _extractionService.SyncEmbeddedSubtitles(episodeEntity);
            episodeEntity.IndexedAt = DateTime.UtcNow;
            
            // Persist the indexing status
            if (saveChanges)
            {
                await _dbContext.SaveChangesAsync();
            }
            _logger.LogDebug("Indexed embedded subtitles for episode {Title}", episodeEntity.Title);
        }
        catch (Exception ex)
        {
            if (ex is DbUpdateException dbEx && dbEx.InnerException is PostgresException pgEx && 
                (pgEx.SqlState == "40001" || pgEx.SqlState == "40P01"))
            {
                _logger.LogWarning("Deadlock/serialization failure detected during embedded subtitle sync for episode {Title}. Rethrowing to utilize execution strategy.", episodeEntity.Title);
                throw;
            }

            _logger.LogWarning(ex, "Failed to index embedded subtitles for episode {Title}", episodeEntity.Title);
        }
    }

    /// <summary>
    /// Removes episodes from the season that no longer exist in Sonarr
    /// </summary>
    private static void RemoveNonExistentEpisodes(Season season, List<SonarrEpisode> currentEpisodes)
    {
        var episodesToRemove = season.Episodes
            .Where(seasonEpisode => currentEpisodes.All(episode => episode.Id != seasonEpisode.SonarrId))
            .ToList();

        foreach (var episodeToRemove in episodesToRemove)
        {
            season.Episodes.Remove(episodeToRemove);
        }
    }
}
