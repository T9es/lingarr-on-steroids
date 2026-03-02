namespace Lingarr.Server.Interfaces.Services;

public interface IInstanceConfigService
{
    Task<InstanceConfig?> GetRadarrConfig(string? instanceId);
    Task<InstanceConfig?> GetSonarrConfig(string? instanceId);
}

public record InstanceConfig(string Url, string ApiKey, string InstanceId);
