using System.Text.Json.Serialization;

namespace Lingarr.Server.Models.Webhooks;

public class RadarrWebhookPayload
{
    [JsonPropertyName("eventType")]
    public string? EventType { get; set; }

    [JsonPropertyName("movie")]
    public RadarrWebhookMovie? Movie { get; set; }

    [JsonPropertyName("remoteMovie")]
    public RadarrRemoteMovie? RemoteMovie { get; set; }

    [JsonPropertyName("release")]
    public RadarrRelease? Release { get; set; }
}

public class RadarrWebhookMovie
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("folderPath")]
    public string? FolderPath { get; set; }

    [JsonPropertyName("tmdbId")]
    public int? TmdbId { get; set; }
}

public class RadarrRemoteMovie
{
    [JsonPropertyName("tmdbId")]
    public int? TmdbId { get; set; }

    [JsonPropertyName("imdbId")]
    public string? ImdbId { get; set; }
}

public class RadarrRelease
{
    [JsonPropertyName("quality")]
    public string? Quality { get; set; }

    [JsonPropertyName("releaseTitle")]
    public string? ReleaseTitle { get; set; }
}
