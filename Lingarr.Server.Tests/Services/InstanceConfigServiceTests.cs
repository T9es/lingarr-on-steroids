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

    private static Mock<ISettingService> CreateSettingServiceMock(Dictionary<string, string?> values)
    {
        var settingServiceMock = new Mock<ISettingService>();
        settingServiceMock
            .Setup(s => s.GetSetting(It.IsAny<string>()))
            .ReturnsAsync((string key) => values.TryGetValue(key, out var value) ? value : null);
        return settingServiceMock;
    }
}
