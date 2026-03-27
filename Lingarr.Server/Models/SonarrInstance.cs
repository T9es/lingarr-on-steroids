namespace Lingarr.Server.Models;

/// <summary>
/// Represents a configured Sonarr instance for multi-instance support.
/// </summary>
public class SonarrInstance
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
    /// The Sonarr server URL (e.g., "http://localhost:8989")
    /// </summary>
    public string Url { get; set; } = string.Empty;
    
    /// <summary>
    /// The Sonarr API key
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
