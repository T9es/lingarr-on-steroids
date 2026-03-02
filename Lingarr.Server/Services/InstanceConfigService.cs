using System.Text.Json;
using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;

namespace Lingarr.Server.Services;

public class InstanceConfigService : IInstanceConfigService
{
    private readonly ISettingService _settingService;
    private readonly ILogger<InstanceConfigService> _logger;

    public InstanceConfigService(ISettingService settingService, ILogger<InstanceConfigService> logger)
    {
        _settingService = settingService;
        _logger = logger;
    }

    public async Task<InstanceConfig?> GetRadarrConfig(string? instanceId)
    {
        return await GetConfig(
            instanceId,
            SettingKeys.Integration.RadarrInstances,
            SettingKeys.Integration.RadarrUrl,
            SettingKeys.Integration.RadarrApiKey,
            "Radarr");
    }

    public async Task<InstanceConfig?> GetSonarrConfig(string? instanceId)
    {
        return await GetConfig(
            instanceId,
            SettingKeys.Integration.SonarrInstances,
            SettingKeys.Integration.SonarrUrl,
            SettingKeys.Integration.SonarrApiKey,
            "Sonarr");
    }

    private async Task<InstanceConfig?> GetConfig(
        string? instanceId,
        string instancesKey,
        string legacyUrlKey,
        string legacyApiKeyKey,
        string serviceName)
    {
        var instancesJson = await _settingService.GetSetting(instancesKey);

        if (!string.IsNullOrEmpty(instancesJson))
        {
            try
            {
                var instances = JsonSerializer.Deserialize<List<InstanceSetting>>(instancesJson);
                if (instances != null && instances.Count > 0)
                {
                    InstanceSetting? instance = null;

                    if (!string.IsNullOrEmpty(instanceId))
                    {
                        instance = instances.FirstOrDefault(i => i.Id == instanceId);
                    }

                    instance ??= instances.FirstOrDefault(i => i.Id == "default") ?? instances[0];

                    if (!string.IsNullOrEmpty(instance.Url) && !string.IsNullOrEmpty(instance.ApiKey))
                    {
                        return new InstanceConfig(instance.Url, instance.ApiKey, instance.Id);
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize {Service} instances from settings", serviceName);
            }
        }

        var url = await _settingService.GetSetting(legacyUrlKey);
        var apiKey = await _settingService.GetSetting(legacyApiKeyKey);

        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("{Service} settings not configured", serviceName);
            return null;
        }

        return new InstanceConfig(url, apiKey, "default");
    }

    private class InstanceSetting
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
    }
}
