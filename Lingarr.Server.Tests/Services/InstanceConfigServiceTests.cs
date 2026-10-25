using System.Collections.Generic;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Lingarr.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class InstanceConfigServiceTests
{
    [Fact]
    public async Task GetRadarrConfig_WhenInstanceIdIsNull_ReturnsDefaultInstance()
    {
        var settingServiceMock = CreateSettingServiceMock(new Dictionary<string, string?>
        {
            [SettingKeys.Integration.RadarrInstances] = """
                [{"id":"default","name":"Radarr","url":"http://radarr","apiKey":"api-key"}]
                """
        });
        var service = new InstanceConfigService(settingServiceMock.Object, NullLogger<InstanceConfigService>.Instance);

        var result = await service.GetRadarrConfig(null);

        Assert.NotNull(result);
        Assert.Equal("default", result!.InstanceId);
        Assert.Equal("http://radarr", result.Url);
    }

    [Fact]
    public async Task GetSonarrConfig_WhenKnownInstanceIdProvided_ReturnsMatchingInstance()
    {
        var settingServiceMock = CreateSettingServiceMock(new Dictionary<string, string?>
        {
            [SettingKeys.Integration.SonarrInstances] = """
                [
                  {"id":"default","name":"Sonarr","url":"http://sonarr-default","apiKey":"default-key"},
                  {"id":"anime","name":"Anime","url":"http://sonarr-anime","apiKey":"anime-key"}
                ]
                """
        });
        var service = new InstanceConfigService(settingServiceMock.Object, NullLogger<InstanceConfigService>.Instance);

        var result = await service.GetSonarrConfig("anime");

        Assert.NotNull(result);
        Assert.Equal("anime", result!.InstanceId);
        Assert.Equal("http://sonarr-anime", result.Url);
    }

    [Fact]
    public async Task GetRadarrConfig_WhenUnknownInstanceIdProvided_ReturnsNull()
    {
        var settingServiceMock = CreateSettingServiceMock(new Dictionary<string, string?>
        {
            [SettingKeys.Integration.RadarrInstances] = """
                [{"id":"default","name":"Radarr","url":"http://radarr","apiKey":"api-key"}]
                """
        });
        var service = new InstanceConfigService(settingServiceMock.Object, NullLogger<InstanceConfigService>.Instance);

        var result = await service.GetRadarrConfig("unknown");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetSonarrConfig_WhenMultiInstanceMissing_FallsBackToLegacySettings()
    {
        var settingServiceMock = CreateSettingServiceMock(new Dictionary<string, string?>
        {
            [SettingKeys.Integration.SonarrInstances] = null,
            [SettingKeys.Integration.SonarrUrl] = "http://legacy-sonarr",
            [SettingKeys.Integration.SonarrApiKey] = "legacy-key"
        });
        var service = new InstanceConfigService(settingServiceMock.Object, NullLogger<InstanceConfigService>.Instance);

        var result = await service.GetSonarrConfig(null);

        Assert.NotNull(result);
        Assert.Equal("default", result!.InstanceId);
        Assert.Equal("http://legacy-sonarr", result.Url);
    }

    [Fact]
    public async Task GetSonarrConfigs_ReturnsEveryValidConfiguredInstance()
    {
        var settingServiceMock = CreateSettingServiceMock(new Dictionary<string, string?>
        {
            [SettingKeys.Integration.SonarrInstances] = """
                [
                  {"id":"default","name":"Sonarr","url":"http://sonarr-default","apiKey":"default-key"},
                  {"id":"anime","name":"Anime","url":"http://sonarr-anime","apiKey":"anime-key"},
                  {"id":"broken","name":"Broken","url":"","apiKey":""}
                ]
                """
        });
        var service = new InstanceConfigService(settingServiceMock.Object, NullLogger<InstanceConfigService>.Instance);

        var result = await service.GetSonarrConfigs();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, instance => instance.InstanceId == "default" && instance.Url == "http://sonarr-default");
        Assert.Contains(result, instance => instance.InstanceId == "anime" && instance.Url == "http://sonarr-anime");
    }

    [Fact]
    public async Task GetSonarrConfigs_WhenNoInstancesConfigured_FallsBackToLegacySettings()
    {
        var settingServiceMock = CreateSettingServiceMock(new Dictionary<string, string?>
        {
            [SettingKeys.Integration.SonarrInstances] = null,
            [SettingKeys.Integration.SonarrUrl] = "http://legacy-sonarr",
            [SettingKeys.Integration.SonarrApiKey] = "legacy-key"
        });
        var service = new InstanceConfigService(settingServiceMock.Object, NullLogger<InstanceConfigService>.Instance);

        var result = await service.GetSonarrConfigs();

        var instance = Assert.Single(result);
        Assert.Equal("default", instance.InstanceId);
        Assert.Equal("http://legacy-sonarr", instance.Url);
    }

    [Fact]
    public async Task GetSonarrConfigs_SkipsInstancesWithBlankIdAndDeduplicatesIds()
    {
        var settingServiceMock = CreateSettingServiceMock(new Dictionary<string, string?>
        {
            [SettingKeys.Integration.SonarrInstances] = """
                [
                  {"id":"default","name":"Sonarr","url":"http://sonarr-first","apiKey":"first-key"},
                  {"id":"default","name":"Sonarr duplicate","url":"http://sonarr-second","apiKey":"second-key"},
                  {"id":"","name":"No id","url":"http://sonarr-no-id","apiKey":"no-id-key"}
                ]
                """
        });
        var service = new InstanceConfigService(settingServiceMock.Object, NullLogger<InstanceConfigService>.Instance);

        var result = await service.GetSonarrConfigs();

        var instance = Assert.Single(result);
        Assert.Equal("default", instance.InstanceId);
        Assert.Equal("http://sonarr-first", instance.Url);
    }

    [Fact]
    public async Task GetSonarrConfigs_WhenInstancesJsonIsInvalid_FallsBackToLegacySettings()
    {
        var settingServiceMock = CreateSettingServiceMock(new Dictionary<string, string?>
        {
            [SettingKeys.Integration.SonarrInstances] = "{not json",
            [SettingKeys.Integration.SonarrUrl] = "http://legacy-sonarr",
            [SettingKeys.Integration.SonarrApiKey] = "legacy-key"
        });
        var service = new InstanceConfigService(settingServiceMock.Object, NullLogger<InstanceConfigService>.Instance);

        var result = await service.GetSonarrConfigs();

        Assert.Equal("http://legacy-sonarr", Assert.Single(result).Url);
    }

    [Fact]
    public async Task GetSonarrConfigs_WhenOnlyInstanceHasNoUrl_FallsBackToLegacySettings()
    {
        var settingServiceMock = CreateSettingServiceMock(new Dictionary<string, string?>
        {
            [SettingKeys.Integration.SonarrInstances] = """
                [{"id":"default","name":"Sonarr","url":"","apiKey":"default-key"}]
                """,
            [SettingKeys.Integration.SonarrUrl] = "http://legacy-sonarr",
            [SettingKeys.Integration.SonarrApiKey] = "legacy-key"
        });
        var service = new InstanceConfigService(settingServiceMock.Object, NullLogger<InstanceConfigService>.Instance);

        var result = await service.GetSonarrConfigs();

        Assert.Equal("http://legacy-sonarr", Assert.Single(result).Url);
    }

    [Fact]
    public async Task GetSonarrConfig_WhenInstancesJsonIsInvalid_FallsBackToLegacySettings()
    {
        var settingServiceMock = CreateSettingServiceMock(new Dictionary<string, string?>
        {
            [SettingKeys.Integration.SonarrInstances] = "{not json",
            [SettingKeys.Integration.SonarrUrl] = "http://legacy-sonarr",
            [SettingKeys.Integration.SonarrApiKey] = "legacy-key"
        });
        var service = new InstanceConfigService(settingServiceMock.Object, NullLogger<InstanceConfigService>.Instance);

        var result = await service.GetSonarrConfig(null);

        Assert.NotNull(result);
        Assert.Equal("http://legacy-sonarr", result!.Url);
    }

    private static Mock<ISettingService> CreateSettingServiceMock(Dictionary<string, string?> values)
    {
        var settingServiceMock = new Mock<ISettingService>();
        settingServiceMock
            .Setup(s => s.GetSetting(It.IsAny<string>()))
            .ReturnsAsync((string key) => values.TryGetValue(key, out var value) ? value : null);
        return settingServiceMock;
    }
}
