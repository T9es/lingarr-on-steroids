using Hangfire;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Webhooks;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Lingarr.Server.Jobs;

public class WebhookJob
{
    private readonly LingarrDbContext _dbContext;
    private readonly IMediaService _mediaService;
    private readonly IMediaSubtitleProcessor _mediaSubtitleProcessor;
    private readonly ISettingService _settingService;
    private readonly ILogger<WebhookJob> _logger;

    public WebhookJob(
        LingarrDbContext dbContext,
        IMediaService mediaService,
        IMediaSubtitleProcessor mediaSubtitleProcessor,
        ISettingService settingService,
        ILogger<WebhookJob> logger)
    {
        _dbContext = dbContext;
        _mediaService = mediaService;
        _mediaSubtitleProcessor = mediaSubtitleProcessor;
        _settingService = settingService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the Radarr instance URL and API key from the instance ID
    /// </summary>
    private async Task<(string Url, string ApiKey)> GetRadarrInstanceConfig(string instanceId)
    {
        var instancesJson = await _settingService.GetSetting(SettingKeys.Integration.RadarrInstances);
        
        if (!string.IsNullOrEmpty(instancesJson))
        {
            try
            {
                var instances = JsonSerializer.Deserialize<List<RadarrInstance>>(
                    instancesJson, 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var instance = instances?.FirstOrDefault(i => i.Id == instanceId);
                if (instance != null)
                {
                    return (instance.Url, instance.ApiKey);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize Radarr instances from settings");
            }
        }

        // Fall back to single instance settings
        var url = await _settingService.GetSetting(SettingKeys.Integration.RadarrUrl);
        var apiKey = await _settingService.GetSetting(SettingKeys.Integration.RadarrApiKey);
        return (url ?? string.Empty, apiKey ?? string.Empty);
    }

    /// <summary>
    /// Gets the Sonarr instance URL and API key from the instance ID
    /// </summary>
    private async Task<(string Url, string ApiKey)> GetSonarrInstanceConfig(string instanceId)
    {
        var instancesJson = await _settingService.GetSetting(SettingKeys.Integration.SonarrInstances);
        
        if (!string.IsNullOrEmpty(instancesJson))
        {
            try
            {
                var instances = JsonSerializer.Deserialize<List<SonarrInstance>>(
                    instancesJson, 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var instance = instances?.FirstOrDefault(i => i.Id == instanceId);
                if (instance != null)
                {
                    return (instance.Url, instance.ApiKey);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize Sonarr instances from settings");
            }
        }

        // Fall back to single instance settings
        var url = await _settingService.GetSetting(SettingKeys.Integration.SonarrUrl);
        var apiKey = await _settingService.GetSetting(SettingKeys.Integration.SonarrApiKey);
        return (url ?? string.Empty, apiKey ?? string.Empty);
    }

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    [AutomaticRetry(Attempts = 3)]
    [Queue("webhook")]
    public async Task ProcessRadarrWebhook(RadarrWebhookPayload payload, string instanceId)
    {
        if (payload.Movie == null || payload.Movie.Id <= 0)
        {
            _logger.LogWarning("Radarr webhook payload missing movie data");
            return;
        }

        var movieId = payload.Movie.Id;
        var title = payload.Movie.Title;
        var eventType = payload.EventType ?? "Unknown";

        _logger.LogInformation(
            "Processing Radarr webhook: {EventType} for '{Title}' (ID: {MovieId}) from instance '{InstanceId}'",
            eventType, title, movieId, instanceId);

        try
        {
            // Validate instance exists before processing
            var (url, apiKey) = await GetRadarrInstanceConfig(instanceId);
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("Webhook received for unknown Radarr instance '{InstanceId}'", instanceId);
                return;
            }

            // Sync or find the movie using the correct instance
            var internalMovieId = await _mediaService.GetMovieIdOrSyncFromRadarrMovieId(movieId, instanceId);
            
            if (internalMovieId == 0)
            {
                _logger.LogWarning(
                    "Movie '{Title}' (Radarr ID: {MovieId}) not found in instance '{InstanceId}' - may not have file yet",
                    title, movieId, instanceId);
                return;
            }

            var movie = await _dbContext.Movies.FirstOrDefaultAsync(m => m.Id == internalMovieId);
            if (movie == null)
            {
                _logger.LogError("Movie with internal ID {InternalId} not found after sync", internalMovieId);
                return;
            }

            // Process subtitles for this movie
            var processed = await _mediaSubtitleProcessor.ProcessMedia(movie, MediaType.Movie);
            
            _logger.LogInformation(
                "Webhook processing complete for '{Title}': subtitles processed = {Processed}",
                title, processed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error processing Radarr webhook for '{Title}' (Radarr ID: {MovieId}) from instance '{InstanceId}'",
                title, movieId, instanceId);
            throw;
        }
    }

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    [AutomaticRetry(Attempts = 3)]
    [Queue("webhook")]
    public async Task ProcessSonarrWebhook(SonarrWebhookPayload payload, string instanceId)
    {
        if (payload.Series == null || payload.Series.Id <= 0)
        {
            _logger.LogWarning("Sonarr webhook payload missing series data");
            return;
        }

        if (payload.Episodes == null || payload.Episodes.Count == 0)
        {
            _logger.LogWarning("Sonarr webhook payload missing episode data");
            return;
        }

        var seriesId = payload.Series.Id;
        var seriesTitle = payload.Series.Title;
        var eventType = payload.EventType ?? "Unknown";

        _logger.LogInformation(
            "Processing Sonarr webhook: {EventType} for '{Title}' (ID: {SeriesId}) from instance '{InstanceId}' - {EpisodeCount} episodes",
            eventType, seriesTitle, seriesId, instanceId, payload.Episodes.Count);

        try
        {
            // Validate instance exists before processing
            var (url, apiKey) = await GetSonarrInstanceConfig(instanceId);
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("Webhook received for unknown Sonarr instance '{InstanceId}'", instanceId);
                return;
            }

            foreach (var episode in payload.Episodes)
            {
                var episodeId = await _mediaService.GetEpisodeIdOrSyncFromSonarrEpisodeId(
                    episode.Id, instanceId);
                
                if (episodeId == 0)
                {
                    _logger.LogWarning(
                        "Episode ID {EpisodeId} for '{SeriesTitle}' not found in instance '{InstanceId}'",
                        episode.Id, seriesTitle, instanceId);
                    continue;
                }

                var episodeEntity = await _dbContext.Episodes
                    .FirstOrDefaultAsync(e => e.Id == episodeId);
                    
                if (episodeEntity == null) continue;

                await _mediaSubtitleProcessor.ProcessMedia(episodeEntity, MediaType.Episode);
            }
            
            _logger.LogInformation(
                "Sonarr webhook processing complete for '{SeriesTitle}'",
                seriesTitle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error processing Sonarr webhook for '{Title}' (Series ID: {SeriesId}) from instance '{InstanceId}'",
                seriesTitle, seriesId, instanceId);
            throw;
        }
    }
}
