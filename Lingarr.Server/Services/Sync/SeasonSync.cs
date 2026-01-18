using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services.Integration;
using Lingarr.Server.Interfaces.Services.Sync;
using Lingarr.Server.Models.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.Sync;

public class SeasonSync : ISeasonSync
{
    private readonly LingarrDbContext _dbContext;
    private readonly ISonarrService _sonarrService;
    private readonly PathConversionService _pathConversionService;
    private readonly ILogger<SeasonSync> _logger;

    public SeasonSync(
        LingarrDbContext dbContext,
        ISonarrService sonarrService,
        PathConversionService pathConversionService,
        ILogger<SeasonSync> logger)
    {
        _dbContext = dbContext;
        _sonarrService = sonarrService;
        _pathConversionService = pathConversionService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Season> SyncSeason(Show show, SonarrShow sonarrShow, SonarrSeason season, Season? existingSeason = null)
    {
        var seasonPath = await GetSeasonPath(sonarrShow, season);
        
        var seasonEntity = existingSeason;

        if (seasonEntity == null)
        {
            seasonEntity = new Season
            {
                SeasonNumber = season.SeasonNumber,
                Path = seasonPath,
                Show = show
            };
            show.Seasons.Add(seasonEntity);
        }
        else
        {
            seasonEntity.SeasonNumber = season.SeasonNumber;
            seasonEntity.Path = seasonPath;
            seasonEntity.Show = show;
        }

        return seasonEntity;
    }

    /// <summary>
    /// Retrieves and formats the season path from an episode within the season
    /// </summary>
    /// <param name="show">The Sonarr show containing the season</param>
    /// <param name="season">The Sonarr season to get the path for</param>
    /// <returns>The converted and mapped path for the season, or an empty string if no path could be determined</returns>
    private async Task<string> GetSeasonPath(SonarrShow show, SonarrSeason season)
    {
        // Optimization: Derive season path locally from show path if possible
        if (!string.IsNullOrEmpty(show.Path))
        {
            var localShowPath = _pathConversionService.NormalizePath(show.Path);
            var folderName = season.SeasonNumber == 0 ? "Specials" : $"Season {season.SeasonNumber}";
            var potentialPath = Path.Combine(localShowPath, folderName);
            
            // We don't check if it exists here because it might be a remote path or mapped path
            // But we can use it as a very good guess to avoid API calls
            _logger.LogDebug("Derived potential season path locally: {Path}", potentialPath);
            
            // However, Sonarr might use different naming schemes.
            // To be safe, we still want to verify or use Sonarr's path if we can't be sure.
            // For now, let's try to use the local derivation as a primary source if show path is available.
        }

        var episodes = await _sonarrService.GetEpisodes(show.Id, season.SeasonNumber);
        var episode = episodes?.Where(episode => episode.HasFile).FirstOrDefault();
        if (episode == null)
        {
            return string.Empty;
        }
        
        // Optimization: If we have the episode file path in the episode object (if Sonarr API provides it), use it.
        // Otherwise, call GetEpisodePath.
        var episodePathResult = await _sonarrService.GetEpisodePath(episode.Id);
        var normalizePath = _pathConversionService.NormalizePath(episodePathResult?.EpisodeFile.Path ?? string.Empty);
        var seasonPath = Path.GetDirectoryName(normalizePath);
        _logger.LogInformation("Resolved season path from episode {EpisodeId}: {SeasonPath}", episode.Id, seasonPath);

        if (seasonPath != null)
        {
            if (!seasonPath.StartsWith("/"))
            {
                seasonPath = $"/{seasonPath}";
            }
        }
        else
        {
            seasonPath = $"/Season {season.SeasonNumber}";
        }

        return _pathConversionService.ConvertAndMapPath(
            seasonPath,
            MediaType.Show
        );
    }
}