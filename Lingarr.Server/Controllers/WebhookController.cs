using Hangfire;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models.Webhooks;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

[ApiController]
[Route("api/webhook")]
public class WebhookController : ControllerBase
{
    private static readonly string[] AllowedWebhookEvents = ["Download"];

    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IBackgroundJobClient backgroundJobClient,
        ILogger<WebhookController> logger)
    {
        _backgroundJobClient = backgroundJobClient;
        _logger = logger;
    }

    /// <summary>
    /// Handles Radarr webhook for movie download events.
    /// URL format: /api/webhook/radarr/{instanceId}
    /// If instanceId omitted, defaults to "default".
    /// </summary>
    [HttpPost("radarr/{instanceId?}")]
    public IActionResult RadarrWebhook(
        [FromBody] RadarrWebhookPayload payload,
        [FromRoute] string? instanceId = null)
    {
        instanceId ??= "default";

        // Check event type before validating payload structure,
        // so non-Download events (Test, Rename, etc.) return 200 OK instead of 400.
        if (!IsAllowedEventType(payload.EventType))
        {
            var eventType = payload.EventType ?? "Unknown";
            _logger.LogInformation(
                "Ignoring Radarr webhook event {EventType} from instance '{InstanceId}'",
                eventType,
                instanceId);
            return Ok(new { message = $"Ignored webhook event type '{eventType}'" });
        }

        if (payload.Movie == null || payload.Movie.Id <= 0)
        {
            _logger.LogWarning("Invalid Radarr webhook payload: missing or invalid movie data");
            return BadRequest(new { message = "Invalid webhook payload: missing movie data" });
        }

        _backgroundJobClient.Enqueue<WebhookJob>(job =>
            job.ProcessRadarrWebhook(payload, instanceId));

        _logger.LogInformation(
            "Queued Radarr webhook for movie '{Title}' (ID: {MovieId}) from instance '{InstanceId}'",
            payload.Movie.Title, payload.Movie.Id, instanceId);

        return Ok(new { message = "Webhook received and queued for processing" });
    }

    /// <summary>
    /// Handles Sonarr webhook for episode download events.
    /// URL format: /api/webhook/sonarr/{instanceId}
    /// If instanceId omitted, defaults to "default".
    /// </summary>
    [HttpPost("sonarr/{instanceId?}")]
    public IActionResult SonarrWebhook(
        [FromBody] SonarrWebhookPayload payload,
        [FromRoute] string? instanceId = null)
    {
        instanceId ??= "default";

        if (payload.Series == null || payload.Series.Id <= 0)
        {
            _logger.LogWarning("Invalid Sonarr webhook payload: missing series data");
            return BadRequest(new { message = "Invalid webhook payload: missing series data" });
        }

        // Check event type before validating episodes, because some non-Download
        // events (SeriesAdd, Rename, etc.) don't include episode data. Returning
        // 400 for those would make Sonarr treat the webhook as broken.
        if (!IsAllowedEventType(payload.EventType))
        {
            var eventType = payload.EventType ?? "Unknown";
            _logger.LogInformation(
                "Ignoring Sonarr webhook event {EventType} for series '{Title}' from instance '{InstanceId}'",
                eventType,
                payload.Series.Title,
                instanceId);
            return Ok(new { message = $"Ignored webhook event type '{eventType}'" });
        }

        if (payload.Episodes == null || payload.Episodes.Count == 0)
        {
            _logger.LogWarning("Invalid Sonarr webhook payload: missing episode data");
            return BadRequest(new { message = "Invalid webhook payload: missing episode data" });
        }

        _backgroundJobClient.Enqueue<WebhookJob>(job =>
            job.ProcessSonarrWebhook(payload, instanceId));

        _logger.LogInformation(
            "Queued Sonarr webhook for series '{Title}' (ID: {SeriesId}) from instance '{InstanceId}' - {Count} episodes",
            payload.Series.Title, payload.Series.Id, instanceId, payload.Episodes.Count);

        return Ok(new { message = "Webhook received and queued for processing" });
    }

    private static bool IsAllowedEventType(string? eventType)
    {
        return AllowedWebhookEvents.Contains(eventType ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }
}
