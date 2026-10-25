using System.Text.Json;
using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;

namespace Lingarr.Server.Services;

public class InstanceConfigService : IInstanceConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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

    public async Task<List<InstanceConfig>> GetSonarrConfigs()
    {
        var instances = (await ReadConfiguredInstances(
                SettingKeys.Integration.SonarrInstances,
                "Sonarr"))
            .Where(instance =>
                !string.IsNullOrWhiteSpace(instance.Id) &&
                !string.IsNullOrEmpty(instance.Url) &&
                !string.IsNullOrEmpty(instance.ApiKey))
            .GroupBy(instance => instance.Id)
            .Select(group => group.First())
            .Select(instance => new InstanceConfig(instance.Url, instance.ApiKey, instance.Id))
            .ToList();

        if (instances.Count > 0)
        {
            return instances;
        }

        var legacy = await GetLegacyConfig(
            SettingKeys.Integration.SonarrUrl,
            SettingKeys.Integration.SonarrApiKey,
            "Sonarr");
        return legacy == null ? [] : [legacy];
    }

    private async Task<InstanceConfig?> GetConfig(
        string? instanceId,
        string instancesKey,
        string legacyUrlKey,
        string legacyApiKeyKey,
        string serviceName)
    {
        var instances = await ReadConfiguredInstances(instancesKey, serviceName);

        if (instances.Count > 0)
        {
            InstanceSetting? instance = null;

            if (!string.IsNullOrEmpty(instanceId))
            {
                instance = instances.FirstOrDefault(i => i.Id == instanceId);
                if (instance == null)
                {
                    _logger.LogWarning(
                        "{Service} instance '{InstanceId}' not found in configured instances",
                        serviceName,
                        instanceId);
                    return null;
                }
            }

            instance ??= instances.FirstOrDefault(i => i.Id == "default") ?? instances[0];

            if (!string.IsNullOrEmpty(instance.Url) && !string.IsNullOrEmpty(instance.ApiKey))
            {
                return new InstanceConfig(instance.Url, instance.ApiKey, instance.Id);
            }
        }

        return await GetLegacyConfig(legacyUrlKey, legacyApiKeyKey, serviceName);
    }

    private async Task<InstanceConfig?> GetLegacyConfig(
        string legacyUrlKey,
        string legacyApiKeyKey,
        string serviceName)
    {
        var url = await _settingService.GetSetting(legacyUrlKey);
        var apiKey = await _settingService.GetSetting(legacyApiKeyKey);

        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("{Service} settings not configured", serviceName);
            return null;
        }

        return new InstanceConfig(url, apiKey, "default");
    }

    private async Task<List<InstanceSetting>> ReadConfiguredInstances(
        string instancesKey,
        string serviceName)
    {
        var instancesJson = await _settingService.GetSetting(instancesKey);
        if (string.IsNullOrEmpty(instancesJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<InstanceSetting>>(instancesJson, JsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize {Service} instances from settings", serviceName);
            return [];
        }
    }
}
