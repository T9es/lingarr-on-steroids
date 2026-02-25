namespace Lingarr.Server.Interfaces.Services;

/// <summary>
/// Result of a cleanup operation
/// </summary>
public class CleanupResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int MoviesReassigned { get; set; }
    public int ShowsReassigned { get; set; }
    public int DuplicatesRemoved { get; set; }
    public int InstancesConsolidated { get; set; }
    public List<string> ReassignedInstanceIds { get; set; } = new();
}

/// <summary>
/// Service for cleaning up duplicate records and consolidating instances
/// </summary>
public interface ICleanupService
{
    /// <summary>
    /// Cleans up duplicate movies/shows and consolidates all media to a single 'default' instance.
    /// Also updates the settings to have only one instance configured.
    /// </summary>
    /// <returns>Result of the cleanup operation</returns>
    Task<CleanupResult> CleanupDuplicateInstances();
}
