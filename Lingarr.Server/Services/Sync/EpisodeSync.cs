using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
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
    public async Task SyncEpisodes(SonarrShow show, Season season, List<Episode>? existingEpisodes = null)
    {
        var episodes = await _sonarrService.GetEpisodes(show.Id, season.SeasonNumber);
        if (episodes == null) return;

        var syncedEpisodes = new List<(Episode Entity, bool NeedsIndexing, string? OldPath, string? OldFileName)>();
        
        // Optimization: Perform a single directory listing per season and use it in memory
        FileInfo[]? seasonFiles = null;
        if (!string.IsNullOrEmpty(season.Path))
        {
            try
            {
                var dirInfo = new DirectoryInfo(season.Path);
                if (dirInfo.Exists)
                {
                    seasonFiles = dirInfo.GetFiles();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to list files for season directory: {Path}", season.Path);
            }
        }

        foreach (var episode in episodes.Where(e => e.HasFile))
        {
            try
            {
                var episodePathResult = await _sonarrService.GetEpisodePath(episode.Id);
                if (episodePathResult == null)
                {
                    _logger.LogWarning("Failed to get episode path for episode {EpisodeId} ({Title})", episode.Id, episode.Title);
                    continue;
                }

                var episodePath = _pathConversionService.ConvertAndMapPath(
                    episodePathResult.EpisodeFile.Path ?? string.Empty,
                    MediaType.Show
                );

                var (entity, needsIndexing, oldPath, oldFileName) = await UpdateEpisodeMetadata(episode, episodePath, season, episodePathResult.EpisodeFile.DateAdded, existingEpisodes, seasonFiles);
                syncedEpisodes.Add((entity, needsIndexing, oldPath, oldFileName));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing episode {EpisodeId} ({Title})", episode.Id, episode.Title);
            }
        }

        // Batch save all metadata updates/additions
        // No longer saving here, deferred to the end of the show or batch

        // Now that IDs might be assigned (if already saved or existing), perform indexing and state updates
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

            // Only index if the episode has been persisted (has a real ID)
            // New episodes will be indexed on the next sync cycle after they're saved
            if (entity.Id > 0 && needsIndexing)
            {
                await IndexEmbeddedSubtitles(entity, saveChanges: false);
            }
        }

        // Use batch state update for all synced episodes
        var episodesToUpdateState = syncedEpisodes
            .Where(x => {
                var entity = x.Entity;
                if (x.NeedsIndexing) return true;
                if (entity.TranslationState != TranslationState.AwaitingSource) return true;
                if (string.IsNullOrEmpty(entity.Path)) return true;
                
                // I/O Caching / Mtime check
                try
                {
                    var dirInfo = new DirectoryInfo(entity.Path);
                    if (dirInfo.Exists)
                    {
                        var dirMtime = dirInfo.LastWriteTimeUtc;
                        if (entity.LastSubtitleCheckAt.HasValue && dirMtime <= entity.LastSubtitleCheckAt.Value)
                        {
                            return false;
                        }
                    }
                }
                catch { /* ignored */ }
                return true;
            })
            .Select(x => (IMedia)x.Entity)
            .ToList();

        if (episodesToUpdateState.Any())
        {
            await _mediaStateService.UpdateStatesAsync(episodesToUpdateState, MediaType.Episode, saveChanges: false);
            foreach (var media in episodesToUpdateState)
            {
                if (media is Episode e) e.LastSubtitleCheckAt = DateTime.UtcNow;
            }
        }

        RemoveNonExistentEpisodes(season, episodes);

        var duplicateEpisodes = season.Episodes.GroupBy(e => e.SonarrId).Where(g => g.Count() > 1).ToList();
        if (duplicateEpisodes.Any())
        {
            foreach (var dup in duplicateEpisodes)
            {
                _logger.LogWarning("Duplicate episode SonarrId found in DB for season {SeasonNumber}: {SonarrId}. Count: {Count}", season.SeasonNumber, dup.Key, dup.Count());
            }
        }
    }

    /// <summary>
    /// Updates or creates the episode entity metadata without saving to DB.
    /// Returns the entity, whether it needs indexing, and old path/filename if changed.
    /// </summary>
    private async Task<(Episode Entity, bool NeedsIndexing, string? OldPath, string? OldFileName)> UpdateEpisodeMetadata(
        SonarrEpisode episode,
        string episodePath,
        Season season,
        DateTime? dateAdded,
        List<Episode>? existingEpisodes = null,
        FileInfo[]? seasonFiles = null)
    {
        var episodeEntity = existingEpisodes?.FirstOrDefault(se => se.SonarrId == episode.Id)
                           ?? season.Episodes.FirstOrDefault(se => se.SonarrId == episode.Id);
        
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
                // Optimization: Use pre-loaded season files if available
                FileInfo? fileInfo = null;
                if (seasonFiles != null)
                {
                    fileInfo = seasonFiles.FirstOrDefault(f =>
                        f.Name.StartsWith(episodeEntity.FileName!) &&
                        !SubtitleExtensions.Contains(f.Extension.ToLowerInvariant()));
                }
                else
                {
                    var dirInfo = new DirectoryInfo(episodeEntity.Path);
                    if (dirInfo.Exists)
                    {
                        fileInfo = dirInfo.GetFiles(episodeEntity.FileName + ".*")
                            .FirstOrDefault(f => !SubtitleExtensions.Contains(f.Extension.ToLowerInvariant()));
                    }
                }
                
                if (fileInfo != null)
                {
                    if (episodeEntity.IndexedAt.HasValue && fileInfo.LastWriteTimeUtc > episodeEntity.IndexedAt.Value.AddSeconds(5))
                    {
                        _logger.LogInformation("Episode file {Title} appears to have been refreshed (mtime changed), triggering re-index", episodeEntity.Title);
                        fileChanged = true;

                        // Clean up stale translated subtitles when media is refreshed
                        if (!string.IsNullOrEmpty(episodeEntity.Path) && !string.IsNullOrEmpty(episodeEntity.FileName))
                        {
                            await _orphanCleanupService.CleanupStaleSubtitlesAsync(
                                episodeEntity.Path,
                                episodeEntity.FileName);
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
