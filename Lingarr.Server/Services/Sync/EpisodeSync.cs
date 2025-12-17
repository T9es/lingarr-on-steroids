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
    private readonly ILogger<EpisodeSync> _logger;

    private readonly LingarrDbContext _dbContext;

    public EpisodeSync(
        ISonarrService sonarrService,
        PathConversionService pathConversionService,
        ISubtitleExtractionService extractionService,
        IMediaStateService mediaStateService,
        ILogger<EpisodeSync> logger,
        LingarrDbContext dbContext)
    {
        _sonarrService = sonarrService;
        _pathConversionService = pathConversionService;
        _extractionService = extractionService;
        _mediaStateService = mediaStateService;
        _logger = logger;
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task SyncEpisodes(SonarrShow show, Season season)
    {
        var episodes = await _sonarrService.GetEpisodes(show.Id, season.SeasonNumber);
        if (episodes == null) return;

        foreach (var episode in episodes.Where(e => e.HasFile))
        {
            var episodePathResult = await _sonarrService.GetEpisodePath(episode.Id);
            var episodePath = _pathConversionService.ConvertAndMapPath(
                episodePathResult?.EpisodeFile.Path ?? string.Empty,
                MediaType.Show
            );
            
            await SyncEpisode(episode, episodePath, season, episodePathResult?.EpisodeFile.DateAdded);
        }

        RemoveNonExistentEpisodes(season, episodes);
    }

    /// <summary>
    /// Synchronizes a single episode with the database, creating or updating the episode entity as needed.
    /// Also indexes embedded subtitles and updates translation state.
    /// </summary>
    private async Task SyncEpisode(SonarrEpisode episode, string episodePath, Season season, DateTime? dateAdded)
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
                DateAdded = dateAdded
            };
            season.Episodes.Add(episodeEntity);
        }
        else
        {
            episodeEntity.EpisodeNumber = episode.EpisodeNumber;
            episodeEntity.Title = episode.Title;
            episodeEntity.FileName = Path.GetFileNameWithoutExtension(episodePath);
            episodeEntity.Path = Path.GetDirectoryName(episodePath);
            episodeEntity.DateAdded = dateAdded;
        }

        // Determine if we need to re-index embedded subtitles
        var fileChanged = !isNew && (
            oldPath != episodeEntity.Path ||
            oldFileName != episodeEntity.FileName);

        var needsIndexing = isNew || fileChanged || episodeEntity.IndexedAt == null;

        if (needsIndexing)
        {
            try
            {
                // Save first so the entity has an ID for the extraction service
                await _dbContext.SaveChangesAsync();

                await _extractionService.SyncEmbeddedSubtitles(episodeEntity);
                episodeEntity.IndexedAt = DateTime.UtcNow;
                
                _logger.LogDebug("Indexed embedded subtitles for episode {Title}", episodeEntity.Title);
            }
            catch (Exception ex)
            {
                // Check if this is a deadlock/serialization failure that we should let bubble up
                // PostgreSQL: 40001 = serialization_failure, 40P01 = deadlock_detected
                if (ex is DbUpdateException dbEx && dbEx.InnerException is PostgresException pgEx && 
                    (pgEx.SqlState == "40001" || pgEx.SqlState == "40P01"))
                {
                    _logger.LogWarning("Deadlock/serialization failure detected during embedded subtitle sync for episode {Title}. Rethrowing to utilize execution strategy.", episodeEntity.Title);
                    throw;
                }

                _logger.LogWarning(ex, "Failed to index embedded subtitles for episode {Title}", episodeEntity.Title);
            }
        }

        // Update translation state
        try
        {
             // If we saved above, we have an ID. If we didn't save above (needsIndexing=false), we assume it's an existing entity (has ID).
             // However, just to be safe (e.g. if logic changes), we might want to ensure ID exists?
             // But existing entities are fetched from DB so they have IDs.
            await _mediaStateService.UpdateStateAsync(episodeEntity, MediaType.Episode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update translation state for episode {Title}", episodeEntity.Title);
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
