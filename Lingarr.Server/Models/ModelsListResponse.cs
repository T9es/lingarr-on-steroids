using System.Text.Json.Serialization;

namespace Lingarr.Server.Models;

/// <summary>
/// Response model for OpenAI's model list endpoint
/// </summary>
public class ModelsListResponse
{
    /// <summary>
    /// The list of models returned by the API
    /// </summary>
    [JsonPropertyName("data")]
    public List<ModelData>? Data { get; set; }

    /// <summary>
    /// The type of object returned
    /// </summary>
    [JsonPropertyName("object")]
    public string? Object { get; set; }
}

/// <summary>
/// Represents a single model in the OpenAI API
/// </summary>
public class ModelData
{
    /// <summary>
    /// The unique identifier for the model
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The type of object
    /// </summary>
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    /// <summary>
    /// When the model was created
    /// </summary>
    [JsonPropertyName("created")]
    public long Created { get; set; }

    /// <summary>
    /// The owner of the model
    /// </summary>
    [JsonPropertyName("owned_by")]
    public string? OwnedBy { get; set; }

    /// <summary>
    /// Chutes-specific chute identifier
    /// </summary>
    [JsonPropertyName("chute_id")]
    public string? ChuteId { get; set; }

    /// <summary>
    /// Pricing data when available.
    /// </summary>
    [JsonPropertyName("price")]
    public ChutesModelPrice? Price { get; set; }
}

public class ChutesModelPrice
{
    [JsonPropertyName("input")]
    public ChutesModelPriceBreakdown? Input { get; set; }

    [JsonPropertyName("output")]
    public ChutesModelPriceBreakdown? Output { get; set; }
}

public class ChutesModelPriceBreakdown
{
    [JsonPropertyName("usd")]
    public decimal? Usd { get; set; }

    [JsonPropertyName("tao")]
    public decimal? Tao { get; set; }
}
