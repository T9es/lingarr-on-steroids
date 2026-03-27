namespace Lingarr.Server.Models;

/// <summary>
/// Represents a configured Radarr instance for multi-instance support.
/// </summary>
public class RadarrInstance
{
    /// <summary>
    /// Unique identifier for this instance (e.g., "default", "4k", "anime")
    /// </summary>
    public string Id { get; set; } = string.Empty;
    
    /// <summary>
    /// Display name for this instance
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// The Radarr server URL (e.g., "http://localhost:7878")
    /// </summary>
    public string Url { get; set; } = string.Empty;
    
    /// <summary>
    /// The Radarr API key
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
