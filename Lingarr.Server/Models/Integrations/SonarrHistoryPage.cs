using System.Text.Json.Serialization;

namespace Lingarr.Server.Models.Integrations;

/// <summary>
/// A page of Sonarr history records.
/// </summary>
public class SonarrHistoryPage
{
    [JsonPropertyName("records")]
    public List<SonarrHistoryRecord> Records { get; set; } = new();
}

/// <summary>
/// A single Sonarr import history record. Only the fields needed to rebuild a
/// download webhook payload are modeled.
/// </summary>
public class SonarrHistoryRecord
{
    [JsonPropertyName("episodeId")]
    public int EpisodeId { get; set; }

    [JsonPropertyName("seriesId")]
    public int SeriesId { get; set; }

    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("series")]
    public SonarrHistorySeries? Series { get; set; }
}

/// <summary>
/// Series data embedded in a Sonarr history record when <c>includeSeries</c> is requested.
/// </summary>
public class SonarrHistorySeries
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("tvdbId")]
    public int? TvdbId { get; set; }
}
