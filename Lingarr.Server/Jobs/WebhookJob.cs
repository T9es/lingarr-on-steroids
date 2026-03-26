using Hangfire;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.Webhooks;

namespace Lingarr.Server.Jobs;

public class WebhookJob
{
    private readonly IAutomationService _automationService;
    private readonly IMediaService _mediaService;
    private readonly IInstanceConfigService _instanceConfigService;
    private readonly ITranslationRequestService _translationRequestService;
    private readonly ILogger<WebhookJob> _logger;

    public WebhookJob(
        IAutomationService automationService,
        IMediaService mediaService,
        IInstanceConfigService instanceConfigService,
        ITranslationRequestService translationRequestService,
        ILogger<WebhookJob> logger)
    {
        _automationService = automationService;
        _mediaService = mediaService;
        _instanceConfigService = instanceConfigService;
        _translationRequestService = translationRequestService;
        _logger = logger;
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
            var config = await _instanceConfigService.GetRadarrConfig(instanceId);
            if (config == null)
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

            var processed = await _automationService.ProcessSingleMediaForAutomationAsync(
                internalMovieId,
                MediaType.Movie,
                "radarr_webhook");
            
            _logger.LogInformation(
                "Webhook processing complete for '{Title}': automation queued = {Processed}",
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
            var config = await _instanceConfigService.GetSonarrConfig(instanceId);
            if (config == null)
            {
                _logger.LogWarning("Webhook received for unknown Sonarr instance '{InstanceId}'", instanceId);
                return;
            }

            foreach (var episode in payload.Episodes)
            {
                var refreshResult = await _mediaService.RefreshEpisodeFromSonarrEpisodeId(
                    episode.Id, instanceId);

                if (refreshResult == null)
                {
                    _logger.LogWarning(
                        "Episode ID {EpisodeId} for '{SeriesTitle}' could not be refreshed in instance '{InstanceId}'",
                        episode.Id, seriesTitle, instanceId);
                    continue;
                }

                if (refreshResult.FileChanged)
                {
                    await _translationRequestService.InterruptActiveRequestsForMedia(
                        MediaType.Episode,
                        refreshResult.EpisodeId);
                }

                await _automationService.ProcessSingleMediaForAutomationAsync(
                    refreshResult.EpisodeId,
                    MediaType.Episode,
                    "sonarr_webhook");
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
