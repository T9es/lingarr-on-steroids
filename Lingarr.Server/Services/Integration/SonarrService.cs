using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Integration;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Integrations;

namespace Lingarr.Server.Services.Integration;

public class SonarrService : ISonarrService
{
    /// <summary>
    /// Sonarr history event type for "download folder imported".
    /// </summary>
    private const int DownloadFolderImportedEventType = 3;

    private readonly IIntegrationService _integrationService;

    public SonarrService(IIntegrationService integrationService)
    {
        _integrationService = integrationService;
    }
    
    /// <inheritdoc />
    public async Task<List<SonarrShow>?> GetShows()
    {
        return await _integrationService.GetApiResponse<List<SonarrShow>>(
            "/api/v3/series/",
            new IntegrationSettingKeys
            {
                Url = "sonarr_url",
                ApiKey = "sonarr_api_key"
            });
    }
    
    /// <inheritdoc />
    public async Task<List<SonarrEpisode>?> GetEpisodes(int seriesNumber, int seasonNumber)
    {
        return await _integrationService.GetApiResponse<List<SonarrEpisode>>(
            $"/api/v3/episode?seriesId={seriesNumber}&seasonNumber={seasonNumber}&includeImages=true",
            new IntegrationSettingKeys
            {
                Url = "sonarr_url",
                ApiKey = "sonarr_api_key"
            });
    }

    /// <inheritdoc />
    public async Task<SonarrEpisode?> GetEpisode(int episodeNumber)
    {
        return await _integrationService.GetApiResponse<SonarrEpisode>(
            $"/api/v3/episode/{episodeNumber}?includeImages=true",
            new IntegrationSettingKeys
            {
                Url = "sonarr_url",
                ApiKey = "sonarr_api_key"
            });
    }
    
    /// <inheritdoc />
    public async Task<SonarrEpisodePath?> GetEpisodePath(int episodeId)
    {
        return await _integrationService.GetApiResponse<SonarrEpisodePath>(
            $"/api/v3/episode/{episodeId}",
            new IntegrationSettingKeys
            {
                Url = "sonarr_url",
                ApiKey = "sonarr_api_key"
            });
    }

    /// <inheritdoc />
    public async Task<List<SonarrShow>?> GetShows(string url, string apiKey)
    {
        return await _integrationService.GetApiResponse<List<SonarrShow>>(
            "/api/v3/series/",
            url,
            apiKey);
    }

    /// <inheritdoc />
    public async Task<List<SonarrEpisode>?> GetEpisodes(int seriesNumber, int seasonNumber, string url, string apiKey)
    {
        return await _integrationService.GetApiResponse<List<SonarrEpisode>>(
            $"/api/v3/episode?seriesId={seriesNumber}&seasonNumber={seasonNumber}&includeImages=true",
            url,
            apiKey);
    }

    /// <inheritdoc />
    public async Task<SonarrEpisode?> GetEpisode(int episodeNumber, string url, string apiKey)
    {
        return await _integrationService.GetApiResponse<SonarrEpisode>(
            $"/api/v3/episode/{episodeNumber}?includeImages=true",
            url,
            apiKey);
    }

    /// <inheritdoc />
    public async Task<SonarrEpisodePath?> GetEpisodePath(int episodeNumber, string url, string apiKey)
    {
        return await _integrationService.GetApiResponse<SonarrEpisodePath>(
            $"/api/v3/episode/{episodeNumber}",
            url,
            apiKey);
    }

    /// <inheritdoc />
    public async Task<SonarrHistoryPage?> GetImportHistory(int page, int pageSize, string url, string apiKey)
    {
        return await _integrationService.GetApiResponse<SonarrHistoryPage>(
            $"/api/v3/history?page={page}&pageSize={pageSize}&sortKey=date&sortDirection=descending" +
            $"&eventType={DownloadFolderImportedEventType}&includeSeries=true",
            url,
            apiKey);
    }
}