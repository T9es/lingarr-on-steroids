using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Jobs;

/// <summary>
/// Tests for the automated translation fallback sweep job.
/// </summary>
public class AutomatedTranslationJobTests
{
    private readonly Mock<IAutomationService> _automationServiceMock;
    private readonly Mock<IScheduleService> _scheduleServiceMock;
    private readonly Mock<ISettingService> _settingServiceMock;
    private readonly Mock<IMediaStateService> _mediaStateServiceMock;
    private readonly AutomatedTranslationJob _job;

    public AutomatedTranslationJobTests()
    {
        _automationServiceMock = new Mock<IAutomationService>();
        _scheduleServiceMock = new Mock<IScheduleService>();
        _settingServiceMock = new Mock<ISettingService>();
        _mediaStateServiceMock = new Mock<IMediaStateService>();

        _job = new AutomatedTranslationJob(
            _automationServiceMock.Object,
            NullLogger<AutomatedTranslationJob>.Instance,
            _scheduleServiceMock.Object,
            _settingServiceMock.Object,
            _mediaStateServiceMock.Object);

        _settingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Automation.AutomationEnabled))
            .ReturnsAsync("true");

        _settingServiceMock
            .Setup(s => s.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                { SettingKeys.Automation.MaxTranslationsPerRun, "10" }
            });
    }

    [Fact]
    public async Task Execute_WhenAutomationDisabled_SkipsProcessing()
    {
        _settingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Automation.AutomationEnabled))
            .ReturnsAsync("false");

        await _job.Execute();

        _mediaStateServiceMock.Verify(
            m => m.GetMediaNeedingTranslationAsync(It.IsAny<int>(), It.IsAny<bool>()),
            Times.Never);
        _automationServiceMock.Verify(
            m => m.ProcessLoadedMediaForAutomationAsync(
                It.IsAny<IMedia>(),
                It.IsAny<MediaType>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task Execute_WhenAutomationEnabled_QueriesMediaStateService()
    {
        _mediaStateServiceMock
            .Setup(m => m.GetMediaNeedingTranslationAsync(It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync([]);

        await _job.Execute();

        _mediaStateServiceMock.Verify(
            m => m.GetMediaNeedingTranslationAsync(20, true),
            Times.Once);
    }

    [Fact]
    public async Task Execute_WithPendingMedia_DelegatesToAutomationService()
    {
        var movie = CreateMovie(1, "Test Movie");

        _mediaStateServiceMock
            .Setup(m => m.GetMediaNeedingTranslationAsync(It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync([(movie, MediaType.Movie)]);

        _automationServiceMock
            .Setup(a => a.ProcessLoadedMediaForAutomationAsync(
                movie,
                MediaType.Movie,
                "fallback_schedule",
                true,
                false))
            .ReturnsAsync(1);

        await _job.Execute();

        _automationServiceMock.Verify(
            a => a.ProcessLoadedMediaForAutomationAsync(
                movie,
                MediaType.Movie,
                "fallback_schedule",
                true,
                false),
            Times.Once);
    }

    [Fact]
    public async Task Execute_RespectsMaxTranslationsPerRun()
    {
        _settingServiceMock
            .Setup(s => s.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                { SettingKeys.Automation.MaxTranslationsPerRun, "2" }
            });

        var movies = new List<(IMedia Media, MediaType Type)>
        {
            (CreateMovie(1, "Movie 1"), MediaType.Movie),
            (CreateMovie(2, "Movie 2"), MediaType.Movie),
            (CreateMovie(3, "Movie 3"), MediaType.Movie)
        };

        _mediaStateServiceMock
            .Setup(m => m.GetMediaNeedingTranslationAsync(It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(movies);

        _automationServiceMock
            .Setup(a => a.ProcessLoadedMediaForAutomationAsync(
                It.IsAny<IMedia>(),
                It.IsAny<MediaType>(),
                "fallback_schedule",
                true,
                false))
            .ReturnsAsync(1);

        await _job.Execute();

        _automationServiceMock.Verify(
            a => a.ProcessLoadedMediaForAutomationAsync(
                It.IsAny<IMedia>(),
                It.IsAny<MediaType>(),
                "fallback_schedule",
                true,
                false),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Execute_ContinuesAfterDirectoryNotFound()
    {
        var firstMovie = CreateMovie(1, "Missing Dir");
        var secondMovie = CreateMovie(2, "Still Processed");

        _mediaStateServiceMock
            .Setup(m => m.GetMediaNeedingTranslationAsync(It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(
            [
                (firstMovie, MediaType.Movie),
                (secondMovie, MediaType.Movie)
            ]);

        _automationServiceMock
            .SetupSequence(a => a.ProcessLoadedMediaForAutomationAsync(
                It.IsAny<IMedia>(),
                It.IsAny<MediaType>(),
                "fallback_schedule",
                true,
                false))
            .ThrowsAsync(new DirectoryNotFoundException())
            .ReturnsAsync(1);

        await _job.Execute();

        _automationServiceMock.Verify(
            a => a.ProcessLoadedMediaForAutomationAsync(
                It.IsAny<IMedia>(),
                It.IsAny<MediaType>(),
                "fallback_schedule",
                true,
                false),
            Times.Exactly(2));
    }

    private static Movie CreateMovie(int id, string title)
    {
        return new Movie
        {
            Id = id,
            RadarrId = id,
            Title = title,
            Path = "/test/path",
            FileName = $"movie{id}",
            DateAdded = DateTime.UtcNow.AddDays(-7),
            TranslationState = TranslationState.Pending
        };
    }
}
