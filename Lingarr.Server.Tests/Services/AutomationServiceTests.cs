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
            false), Times.Once);
    }
}
