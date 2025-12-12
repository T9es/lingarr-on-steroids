namespace Lingarr.Server.Models;

/// <summary>
/// Represents the result of testing a connection to an integration service (Radarr/Sonarr).
/// </summary>
public class IntegrationTestResult
{
    /// <summary>
    /// Indicates whether the connection was successful.
    /// </summary>
    public bool IsConnected { get; set; }
    
    /// <summary>
    /// A descriptive message about the connection status or error.
    /// </summary>
    public string? Message { get; set; }
    
    /// <summary>
    /// The version of the connected service, if available.
    /// </summary>
    public string? Version { get; set; }
}
