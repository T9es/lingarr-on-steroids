using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class AutomationServiceTests
{
    [Fact]
    public async Task ProcessLoadedMediaForAutomationAsync_ShouldQueueForStaleMediaUsingForceProcess()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new LingarrDbContext(options);
        var mediaSubtitleProcessorMock = new Mock<IMediaSubtitleProcessor>();
        var customMediaSubtitleProcessorMock = new Mock<ICustomMediaSubtitleProcessor>();
        var settingServiceMock = new Mock<ISettingService>();
        var mediaStateServiceMock = new Mock<IMediaStateService>();
        var customMediaStateServiceMock = new Mock<ICustomMediaStateService>();

        settingServiceMock
            .Setup(s => s.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                [SettingKeys.Automation.AutomationEnabled] = "true",
                [SettingKeys.Automation.MovieAgeThreshold] = "0",
                [SettingKeys.Automation.ShowAgeThreshold] = "0"
            });

        mediaStateServiceMock
            .Setup(s => s.GetSettingsVersionAsync())
            .ReturnsAsync(1);

        mediaStateServiceMock
            .Setup(s => s.UpdateStateAsync(It.IsAny<Movie>(), MediaType.Movie, true))
            .ReturnsAsync(TranslationState.Stale);

        mediaSubtitleProcessorMock
            .Setup(s => s.ProcessMediaForceAsync(
                It.IsAny<Movie>(),
                MediaType.Movie,
                true,
                false,
                true,
                true,
                null))
            .ReturnsAsync(1);

        var service = new AutomationService(
            dbContext,
            mediaSubtitleProcessorMock.Object,
            customMediaSubtitleProcessorMock.Object,
            settingServiceMock.Object,
            mediaStateServiceMock.Object,
            customMediaStateServiceMock.Object,
            NullLogger<AutomationService>.Instance);

        var movie = new Movie
        {
            Id = 1,
            RadarrId = 1,
            Title = "Stale Movie",
            Path = "/movies",
            FileName = "stale.mkv",
            DateAdded = DateTime.UtcNow.AddDays(-2),
            TranslationState = TranslationState.Stale,
            StateSettingsVersion = 1
        };

        var queued = await service.ProcessLoadedMediaForAutomationAsync(
            movie,
            MediaType.Movie,
            "unit_test",
            updateRotationTimestamp: false,
            forceStateRefresh: false);

        Assert.Equal(1, queued);
        mediaSubtitleProcessorMock.Verify(s => s.ProcessMediaForceAsync(
            It.IsAny<Movie>(),
            MediaType.Movie,
            true,
            false,
            true,
            true,
            null), Times.Once);
    }

    [Fact]
    public async Task ProcessLoadedMediaForAutomationAsync_WithCompletedCustomItem_RefreshesStateAndQueuesWhenStale()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new LingarrDbContext(options);
        var customSource = new CustomSource
        {
            Id = 1,
            Name = "Custom Source",
            SourceType = CustomSourceType.MovieRoot,
            RootPath = @"C:\media\custom",
            Recursive = true,
            Enabled = true,
            IncludeInAutomation = true
        };
        var item = new CustomMediaItem
        {
            Id = 10,
            CustomSourceId = customSource.Id,
            CustomSource = customSource,
            ItemKind = CustomMediaItemKind.Movie,
            Title = "Completed Custom Movie",
            FileName = "completed.mkv",
            Path = @"C:\media\custom\completed.mkv",
            RelativePath = "completed.mkv",
            DateAdded = DateTime.UtcNow.AddDays(-2),
            TranslationState = TranslationState.Complete,
            StateSettingsVersion = 1
        };
        dbContext.CustomSources.Add(customSource);
        dbContext.CustomMediaItems.Add(item);
        await dbContext.SaveChangesAsync();

        var mediaSubtitleProcessorMock = new Mock<IMediaSubtitleProcessor>();
        var customMediaSubtitleProcessorMock = new Mock<ICustomMediaSubtitleProcessor>();
        var settingServiceMock = new Mock<ISettingService>();
        var mediaStateServiceMock = new Mock<IMediaStateService>();
        var customMediaStateServiceMock = new Mock<ICustomMediaStateService>();

        settingServiceMock
            .Setup(s => s.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                [SettingKeys.Automation.AutomationEnabled] = "true",
                [SettingKeys.Automation.MovieAgeThreshold] = "0",
                [SettingKeys.Automation.ShowAgeThreshold] = "0"
            });

        customMediaStateServiceMock
            .Setup(s => s.GetSettingsVersionAsync())
            .ReturnsAsync(1);
        customMediaStateServiceMock
            .Setup(s => s.UpdateStateAsync(It.IsAny<CustomMediaItem>(), true))
            .ReturnsAsync(TranslationState.Stale);

        customMediaSubtitleProcessorMock
            .Setup(s => s.ProcessCustomItemForceAsync(
                It.IsAny<CustomMediaItem>(),
                true,
                false,
                false))
            .ReturnsAsync(1);

        var service = new AutomationService(
            dbContext,
            mediaSubtitleProcessorMock.Object,
            customMediaSubtitleProcessorMock.Object,
            settingServiceMock.Object,
            mediaStateServiceMock.Object,
            customMediaStateServiceMock.Object,
            NullLogger<AutomationService>.Instance);

        var queued = await service.ProcessLoadedMediaForAutomationAsync(
            item,
            MediaType.Movie,
            "unit_test",
            updateRotationTimestamp: false,
            forceStateRefresh: false);

        Assert.Equal(1, queued);
        customMediaStateServiceMock.Verify(
            s => s.UpdateStateAsync(It.Is<CustomMediaItem>(candidate => candidate.Id == item.Id), true),
            Times.AtLeastOnce);
        customMediaSubtitleProcessorMock.Verify(
            s => s.ProcessCustomItemForceAsync(
                It.Is<CustomMediaItem>(candidate => candidate.Id == item.Id),
                true,
                false,
                false),
            Times.Once);
    }

    [Fact]
    public async Task ProcessLoadedMediaForAutomationAsync_WithCompletedCustomItemThatStaysComplete_UpdatesRotationTimestamp()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new LingarrDbContext(options);
        var customSource = new CustomSource
        {
            Id = 2,
            Name = "Custom Source",
            SourceType = CustomSourceType.MovieRoot,
            RootPath = @"C:\media\custom",
            Recursive = true,
            Enabled = true,
            IncludeInAutomation = true
        };
        var item = new CustomMediaItem
        {
            Id = 20,
            CustomSourceId = customSource.Id,
            CustomSource = customSource,
            ItemKind = CustomMediaItemKind.Movie,
            Title = "Stable Custom Movie",
            FileName = "stable.mkv",
            Path = @"C:\media\custom\stable.mkv",
            RelativePath = "stable.mkv",
            DateAdded = DateTime.UtcNow.AddDays(-2),
            TranslationState = TranslationState.Complete,
            StateSettingsVersion = 1
        };
        dbContext.CustomSources.Add(customSource);
        dbContext.CustomMediaItems.Add(item);
        await dbContext.SaveChangesAsync();

        var mediaSubtitleProcessorMock = new Mock<IMediaSubtitleProcessor>();
        var customMediaSubtitleProcessorMock = new Mock<ICustomMediaSubtitleProcessor>();
        var settingServiceMock = new Mock<ISettingService>();
        var mediaStateServiceMock = new Mock<IMediaStateService>();
        var customMediaStateServiceMock = new Mock<ICustomMediaStateService>();

        settingServiceMock
            .Setup(s => s.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                [SettingKeys.Automation.AutomationEnabled] = "true",
                [SettingKeys.Automation.MovieAgeThreshold] = "0",
                [SettingKeys.Automation.ShowAgeThreshold] = "0"
            });

        customMediaStateServiceMock
            .Setup(s => s.GetSettingsVersionAsync())
            .ReturnsAsync(1);
        customMediaStateServiceMock
            .Setup(s => s.UpdateStateAsync(It.IsAny<CustomMediaItem>(), true))
            .ReturnsAsync(TranslationState.Complete);

        var service = new AutomationService(
            dbContext,
            mediaSubtitleProcessorMock.Object,
            customMediaSubtitleProcessorMock.Object,
            settingServiceMock.Object,
            mediaStateServiceMock.Object,
            customMediaStateServiceMock.Object,
            NullLogger<AutomationService>.Instance);

        var queued = await service.ProcessLoadedMediaForAutomationAsync(
            item,
            MediaType.Movie,
            "unit_test",
            updateRotationTimestamp: true,
            forceStateRefresh: false);

        Assert.Equal(0, queued);
        customMediaStateServiceMock.Verify(
            s => s.UpdateLastSubtitleCheckAt(item.Id),
            Times.Once);
        customMediaSubtitleProcessorMock.Verify(
            s => s.ProcessCustomItemForceAsync(
                It.IsAny<CustomMediaItem>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessLoadedMediaForAutomationAsync_WithPendingCustomItemThatRefreshesToComplete_UpdatesRotationTimestamp()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new LingarrDbContext(options);
        var customSource = new CustomSource
        {
            Id = 3,
            Name = "Custom Source",
            SourceType = CustomSourceType.MovieRoot,
            RootPath = @"C:\media\custom",
            Recursive = true,
            Enabled = true,
            IncludeInAutomation = true
        };
        var item = new CustomMediaItem
        {
            Id = 30,
            CustomSourceId = customSource.Id,
            CustomSource = customSource,
            ItemKind = CustomMediaItemKind.Movie,
            Title = "Freshly Rechecked Custom Movie",
            FileName = "fresh.mkv",
            Path = @"C:\media\custom\fresh.mkv",
            RelativePath = "fresh.mkv",
            DateAdded = DateTime.UtcNow.AddDays(-2),
            TranslationState = TranslationState.Pending,
            StateSettingsVersion = 1
        };
        dbContext.CustomSources.Add(customSource);
        dbContext.CustomMediaItems.Add(item);
        await dbContext.SaveChangesAsync();

        var mediaSubtitleProcessorMock = new Mock<IMediaSubtitleProcessor>();
        var customMediaSubtitleProcessorMock = new Mock<ICustomMediaSubtitleProcessor>();
        var settingServiceMock = new Mock<ISettingService>();
        var mediaStateServiceMock = new Mock<IMediaStateService>();
        var customMediaStateServiceMock = new Mock<ICustomMediaStateService>();

        settingServiceMock
            .Setup(s => s.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                [SettingKeys.Automation.AutomationEnabled] = "true",
                [SettingKeys.Automation.MovieAgeThreshold] = "0",
                [SettingKeys.Automation.ShowAgeThreshold] = "0"
            });

        customMediaStateServiceMock
            .Setup(s => s.GetSettingsVersionAsync())
            .ReturnsAsync(2);
        customMediaStateServiceMock
            .Setup(s => s.UpdateStateAsync(It.IsAny<CustomMediaItem>(), true))
            .ReturnsAsync(TranslationState.Complete);

        var service = new AutomationService(
            dbContext,
            mediaSubtitleProcessorMock.Object,
            customMediaSubtitleProcessorMock.Object,
            settingServiceMock.Object,
            mediaStateServiceMock.Object,
            customMediaStateServiceMock.Object,
            NullLogger<AutomationService>.Instance);

        var queued = await service.ProcessLoadedMediaForAutomationAsync(
            item,
            MediaType.Movie,
            "unit_test",
            updateRotationTimestamp: true,
            forceStateRefresh: false);

        Assert.Equal(0, queued);
        customMediaStateServiceMock.Verify(
            s => s.UpdateLastSubtitleCheckAt(item.Id),
            Times.Once);
        customMediaSubtitleProcessorMock.Verify(
            s => s.ProcessCustomItemForceAsync(
                It.IsAny<CustomMediaItem>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()),
            Times.Never);
    }
}
