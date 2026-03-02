using System.Text.Json.Serialization;

namespace Lingarr.Server.Models.Webhooks;

public class SonarrWebhookPayload
{
    [JsonPropertyName("eventType")]
    public string? EventType { get; set; }

    [JsonPropertyName("series")]
    public SonarrWebhookSeries? Series { get; set; }

    [JsonPropertyName("episodes")]
    public List<SonarrWebhookEpisode>? Episodes { get; set; }
}

public class SonarrWebhookSeries
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("tvdbId")]
    public int? TvdbId { get; set; }
}

public class SonarrWebhookEpisode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("episodeNumber")]
    public int EpisodeNumber { get; set; }

    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}
