using System;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Hubs;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Listener;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class SettingChangedListenerTests
{
    [Fact]
    public async Task OnSettingChanged_WithSkipWhenTargetEmbedded_InvalidateTranslationState()
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var seedContext = new LingarrDbContext(options))
        {
            seedContext.Settings.AddRange(
                new Setting { Key = SettingKeys.Translation.SourceLanguages, Value = "[{\"code\":\"en\",\"name\":\"English\"}]" },
                new Setting { Key = SettingKeys.Translation.TargetLanguages, Value = "[{\"code\":\"pl\",\"name\":\"Polish\"}]" },
                new Setting { Key = SettingKeys.Translation.IgnoreCaptions, Value = "false" },
                new Setting { Key = SettingKeys.Translation.SubtitleOutputMode, Value = "match-source" },
                new Setting { Key = SettingKeys.SubtitleValidation.SkipWhenTargetEmbedded, Value = "true" });
            await seedContext.SaveChangesAsync();
        }

        var mediaStateServiceMock = new Mock<IMediaStateService>();
        var invalidationCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var versionIncremented = false;
        var staleMarked = false;

        mediaStateServiceMock
            .Setup(service => service.IncrementSettingsVersionAsync())
            .Callback(() =>
            {
                versionIncremented = true;
                if (staleMarked)
                {
                    invalidationCompleted.TrySetResult();
                }
            })
            .Returns(Task.CompletedTask);
        mediaStateServiceMock
            .Setup(service => service.MarkAllStaleAsync())
            .Callback(() =>
            {
                staleMarked = true;
                if (versionIncremented)
                {
                    invalidationCompleted.TrySetResult();
                }
            })
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddDbContext<LingarrDbContext>(builder => builder.UseInMemoryDatabase(databaseName));
        services.AddScoped(_ => Mock.Of<ISettingService>());
        services.AddScoped(_ => mediaStateServiceMock.Object);
        var provider = services.BuildServiceProvider();

        var listener = new SettingChangedListener(
            provider,
            Mock.Of<IScheduleService>(),
            Mock.Of<IHubContext<SettingUpdatesHub>>(),
            NullLogger<SettingChangedListener>.Instance);

        listener.OnSettingChanged(null!, SettingKeys.SubtitleValidation.SkipWhenTargetEmbedded);

        await invalidationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        mediaStateServiceMock.Verify(service => service.IncrementSettingsVersionAsync(), Times.Once);
        mediaStateServiceMock.Verify(service => service.MarkAllStaleAsync(), Times.Once);
    }

    [Fact]
    public async Task OnSettingChanged_WithSourceLanguages_AlsoInvalidatesTranslationState()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options;

        await using (var seedContext = new LingarrDbContext(options))
        {
            await seedContext.Database.EnsureCreatedAsync();
            seedContext.Settings.AddRange(
                new Setting { Key = SettingKeys.Translation.SourceLanguages, Value = "[{\"code\":\"en\",\"name\":\"English\"}]" },
                new Setting { Key = SettingKeys.Translation.TargetLanguages, Value = "[{\"code\":\"pl\",\"name\":\"Polish\"}]" },
                new Setting { Key = SettingKeys.Translation.IgnoreCaptions, Value = "false" },
                new Setting { Key = SettingKeys.Translation.SubtitleOutputMode, Value = "match-source" },
                new Setting { Key = SettingKeys.SubtitleValidation.SkipWhenTargetEmbedded, Value = "true" });
            await seedContext.SaveChangesAsync();
        }

        var mediaStateServiceMock = new Mock<IMediaStateService>();
        var invalidationCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var versionIncremented = false;
        var staleMarked = false;

        mediaStateServiceMock
            .Setup(service => service.IncrementSettingsVersionAsync())
            .Callback(() =>
            {
                versionIncremented = true;
                if (staleMarked)
                {
                    invalidationCompleted.TrySetResult();
                }
            })
            .Returns(Task.CompletedTask);
        mediaStateServiceMock
            .Setup(service => service.MarkAllStaleAsync())
            .Callback(() =>
            {
                staleMarked = true;
                if (versionIncremented)
                {
                    invalidationCompleted.TrySetResult();
                }
            })
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddDbContext<LingarrDbContext>(builder => builder.UseSqlite(connection).UseSnakeCaseNamingConvention());
        services.AddScoped(_ => Mock.Of<ISettingService>());
        services.AddScoped(_ => mediaStateServiceMock.Object);
        var provider = services.BuildServiceProvider();

        var listener = new SettingChangedListener(
            provider,
            Mock.Of<IScheduleService>(),
            Mock.Of<IHubContext<SettingUpdatesHub>>(),
            NullLogger<SettingChangedListener>.Instance);

        listener.OnSettingChanged(null!, SettingKeys.Translation.SourceLanguages);

        await invalidationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        mediaStateServiceMock.Verify(service => service.IncrementSettingsVersionAsync(), Times.Once);
        mediaStateServiceMock.Verify(service => service.MarkAllStaleAsync(), Times.Once);
    }
}
