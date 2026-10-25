namespace Lingarr.Server.Interfaces.Services;

public interface IInstanceConfigService
{
    Task<InstanceConfig?> GetRadarrConfig(string? instanceId);
    Task<InstanceConfig?> GetSonarrConfig(string? instanceId);

    /// <summary>
    /// Returns every configured Sonarr instance, falling back to the legacy single instance
    /// settings. Instances with a missing id, URL or API key are omitted, and duplicate ids
    /// are collapsed to the first entry.
    /// </summary>
    Task<List<InstanceConfig>> GetSonarrConfigs();
}

public record InstanceConfig(string Url, string ApiKey, string InstanceId);
