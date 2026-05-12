using System.Text.Json.Serialization;

namespace Lingarr.Server.Models.Batch;

/// <summary>
/// Represents a subtitle item in a batch translation request
/// </summary>
public class BatchSubtitleItem
{
    /// <summary>
    /// Position or index identifier of the subtitle
    /// </summary>
    [JsonPropertyName("position")]
    public int Position { get; set; }

    /// <summary>
    /// Short source fingerprint used to validate provider response alignment
    /// </summary>
    [JsonPropertyName("sourceKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceKey { get; set; }

    /// <summary>
    /// Line to translate
    /// </summary>
    [JsonPropertyName("line")]
    public required string Line { get; set; }
}
