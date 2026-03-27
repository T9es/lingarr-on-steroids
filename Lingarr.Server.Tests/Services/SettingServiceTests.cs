using System;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Hubs;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Listener;
using Lingarr.Server.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class SettingServiceTests
{
    [Fact]
    public async Task SetSetting_ShouldEncryptSensitiveValuesBeforePersisting()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, out var encryptionService);

        await service.SetSetting(SettingKeys.Translation.OpenAi.ApiKey, "secret-value");

        var stored = await dbContext.Settings.SingleAsync(s => s.Key == SettingKeys.Translation.OpenAi.ApiKey);
        Assert.NotEqual("secret-value", stored.Value);
        Assert.Equal("secret-value", encryptionService.Decrypt(stored.Value));

        var value = await service.GetEncryptedSetting(SettingKeys.Translation.OpenAi.ApiKey);
        Assert.Equal("secret-value", value);
    }

    [Fact]
    public async Task GetSetting_ShouldMigrateLegacyPlaintextSecretToEncryptedStorage()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Settings.AddAsync(new Setting
        {
            Key = SettingKeys.Translation.OpenAi.ApiKey,
            Value = "legacy-plaintext"
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, out var encryptionService);

        var value = await service.GetSetting(SettingKeys.Translation.OpenAi.ApiKey);
        Assert.Equal("legacy-plaintext", value);

        var stored = await dbContext.Settings.SingleAsync(s => s.Key == SettingKeys.Translation.OpenAi.ApiKey);
        Assert.NotEqual("legacy-plaintext", stored.Value);
        Assert.Equal("legacy-plaintext", encryptionService.Decrypt(stored.Value));
    }

    [Fact]
    public async Task GetSettingAsJson_ShouldReturnDecryptedInstanceConfiguration()
    {
        await using var dbContext = CreateDbContext();
        var legacyJson = """
            [{"id":"default","name":"Radarr","url":"http://radarr","apiKey":"top-secret"}]
            """;
        await dbContext.Settings.AddAsync(new Setting
        {
            Key = SettingKeys.Integration.RadarrInstances,
            Value = legacyJson
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, out var encryptionService);

        var instances = await service.GetSettingAsJson<TestInstanceSetting>(SettingKeys.Integration.RadarrInstances);

        var instance = Assert.Single(instances);
        Assert.Equal("default", instance.Id);
        Assert.Equal("top-secret", instance.ApiKey);

        var stored = await dbContext.Settings.SingleAsync(s => s.Key == SettingKeys.Integration.RadarrInstances);
        Assert.NotEqual(legacyJson, stored.Value);
        Assert.Equal(legacyJson, encryptionService.Decrypt(stored.Value));
    }

    [Fact]
    public async Task SetEncryptedSetting_AndGetEncryptedSettings_ShouldRoundTripNonSensitiveKeys()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, out var encryptionService);

        await service.SetEncryptedSetting("custom_secret", "very-secret");
        await service.SetSetting("plain_setting", "plain-value");

        var storedSecret = await dbContext.Settings.SingleAsync(s => s.Key == "custom_secret");
        Assert.NotEqual("very-secret", storedSecret.Value);
        Assert.Equal("very-secret", encryptionService.Decrypt(storedSecret.Value));

        var settings = await service.GetEncryptedSettings(["custom_secret", "plain_setting", "missing_setting"]);
        Assert.Equal("very-secret", settings["custom_secret"]);
        Assert.Equal("plain-value", settings["plain_setting"]);
        Assert.Equal(string.Empty, settings["missing_setting"]);
    }

    private static LingarrDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LingarrDbContext(options);
    }

    private static SettingService CreateService(LingarrDbContext dbContext, out IEncryptionService encryptionService)
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        using var provider = services.BuildServiceProvider();
        encryptionService = new EncryptionService(provider.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>());

        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var listener = new SettingChangedListener(
            new ServiceCollection().BuildServiceProvider(),
            Mock.Of<IScheduleService>(),
            Mock.Of<IHubContext<SettingUpdatesHub>>(),
            Mock.Of<ILogger<SettingChangedListener>>());

        return new SettingService(
            dbContext,
            Mock.Of<ILogger<ISettingService>>(),
            memoryCache,
            listener,
            encryptionService);
    }

    private sealed class TestInstanceSetting
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
    }
}
