using System.Text.Json.Serialization;

namespace Lingarr.Server.Models.Batch.Response;

/// <summary>
/// Translated subtitle result model
/// </summary>
public class StructuredBatchResponse
{
    [JsonPropertyName("line")]
    public string Line { get; set; } = string.Empty;

    [JsonPropertyName("sourceKey")]
    public string? SourceKey { get; set; }
    
    [JsonPropertyName("position")]
    public int Position { get; set; }
}
