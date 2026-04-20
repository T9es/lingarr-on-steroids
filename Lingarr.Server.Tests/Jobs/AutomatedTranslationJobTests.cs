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
    private readonly Mock<ICustomMediaStateService> _customMediaStateServiceMock;
    private readonly AutomatedTranslationJob _job;

    public AutomatedTranslationJobTests()
    {
        _automationServiceMock = new Mock<IAutomationService>();
        _scheduleServiceMock = new Mock<IScheduleService>();
        _settingServiceMock = new Mock<ISettingService>();
        _mediaStateServiceMock = new Mock<IMediaStateService>();
        _customMediaStateServiceMock = new Mock<ICustomMediaStateService>();

        _job = new AutomatedTranslationJob(
            _automationServiceMock.Object,
            NullLogger<AutomatedTranslationJob>.Instance,
            _scheduleServiceMock.Object,
            _settingServiceMock.Object,
            _mediaStateServiceMock.Object,
            _customMediaStateServiceMock.Object);

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

    [Fact]
    public async Task Execute_WithLibraryBacklog_ProcessesReservedCustomCandidateBeforeStoppingAtLimit()
    {
        _settingServiceMock
            .Setup(s => s.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                { SettingKeys.Automation.MaxTranslationsPerRun, "2" }
            });
        _settingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Automation.TranslationCycle))
            .ReturnsAsync("custom");
        _settingServiceMock
            .Setup(s => s.SetSetting(SettingKeys.Automation.TranslationCycle, "library"))
            .ReturnsAsync(true);

        var libraryMovieOne = CreateMovie(1, "Library 1");
        var libraryMovieTwo = CreateMovie(2, "Library 2");
        var customItem = new CustomMediaItem
        {
            Id = 50,
            CustomSourceId = 5,
            CustomSource = new CustomSource
            {
                Id = 5,
                Name = "Custom Source",
                SourceType = CustomSourceType.MovieRoot,
                RootPath = @"C:\custom",
                Recursive = true,
                Enabled = true,
                IncludeInAutomation = true
            },
            ItemKind = CustomMediaItemKind.Movie,
            Title = "Custom Item",
            FileName = "custom.mkv",
            Path = @"C:\custom\custom.mkv",
            RelativePath = "custom.mkv",
            DateAdded = DateTime.UtcNow.AddDays(-3),
            TranslationState = TranslationState.Complete
        };

        _mediaStateServiceMock
            .Setup(m => m.GetMediaNeedingTranslationAsync(It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(
            [
                (libraryMovieOne, MediaType.Movie),
                (libraryMovieTwo, MediaType.Movie)
            ]);
        _customMediaStateServiceMock
            .Setup(m => m.GetItemsNeedingTranslationAsync(It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync([customItem]);

        var invocationOrder = new List<string>();
        _automationServiceMock
            .Setup(a => a.ProcessLoadedMediaForAutomationAsync(
                customItem,
                MediaType.Movie,
                "fallback_schedule",
                true,
                false))
            .Callback(() => invocationOrder.Add("custom"))
            .ReturnsAsync(1);
        _automationServiceMock
            .Setup(a => a.ProcessLoadedMediaForAutomationAsync(
                libraryMovieOne,
                MediaType.Movie,
                "fallback_schedule",
                true,
                false))
            .Callback(() => invocationOrder.Add("library-1"))
            .ReturnsAsync(1);
        _automationServiceMock
            .Setup(a => a.ProcessLoadedMediaForAutomationAsync(
                libraryMovieTwo,
                MediaType.Movie,
                "fallback_schedule",
                true,
                false))
            .Callback(() => invocationOrder.Add("library-2"))
            .ReturnsAsync(1);

        await _job.Execute();

        Assert.Equal(new[] { "custom", "library-1" }, invocationOrder);
        _settingServiceMock.Verify(
            s => s.SetSetting(SettingKeys.Automation.TranslationCycle, "library"),
            Times.Once);
    }

    [Fact]
    public async Task Execute_WithSingleTranslationBudgetAndCustomCycle_PrioritizesCustomCandidate()
    {
        _settingServiceMock
            .Setup(s => s.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                { SettingKeys.Automation.MaxTranslationsPerRun, "1" }
            });
        _settingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Automation.TranslationCycle))
            .ReturnsAsync("custom");
        _settingServiceMock
            .Setup(s => s.SetSetting(SettingKeys.Automation.TranslationCycle, "library"))
            .ReturnsAsync(true);

        var libraryMovie = CreateMovie(1, "Library 1");
        var customItem = new CustomMediaItem
        {
            Id = 60,
            CustomSourceId = 6,
            CustomSource = new CustomSource
            {
                Id = 6,
                Name = "Custom Source",
                SourceType = CustomSourceType.MovieRoot,
                RootPath = @"C:\custom",
                Recursive = true,
                Enabled = true,
                IncludeInAutomation = true
            },
            ItemKind = CustomMediaItemKind.Movie,
            Title = "Custom Single Budget",
            FileName = "custom-single.mkv",
            Path = @"C:\custom\custom-single.mkv",
            RelativePath = "custom-single.mkv",
            DateAdded = DateTime.UtcNow.AddDays(-3),
            TranslationState = TranslationState.Complete
        };

        _mediaStateServiceMock
            .Setup(m => m.GetMediaNeedingTranslationAsync(It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync([(libraryMovie, MediaType.Movie)]);
        _customMediaStateServiceMock
            .Setup(m => m.GetItemsNeedingTranslationAsync(It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync([customItem]);

        var invocationOrder = new List<string>();
        _automationServiceMock
            .Setup(a => a.ProcessLoadedMediaForAutomationAsync(
                customItem,
                MediaType.Movie,
                "fallback_schedule",
                true,
                false))
            .Callback(() => invocationOrder.Add("custom"))
            .ReturnsAsync(1);
        _automationServiceMock
            .Setup(a => a.ProcessLoadedMediaForAutomationAsync(
                libraryMovie,
                MediaType.Movie,
                "fallback_schedule",
                true,
                false))
            .Callback(() => invocationOrder.Add("library"))
            .ReturnsAsync(1);

        await _job.Execute();

        Assert.Equal(new[] { "custom" }, invocationOrder);
        _settingServiceMock.Verify(
            s => s.SetSetting(SettingKeys.Automation.TranslationCycle, "library"),
            Times.Once);
    }

    [Fact]
    public async Task Execute_WithSingleTranslationBudgetAndLibraryCycle_PrioritizesLibraryCandidate()
    {
        _settingServiceMock
            .Setup(s => s.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                { SettingKeys.Automation.MaxTranslationsPerRun, "1" }
            });
        _settingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Automation.TranslationCycle))
            .ReturnsAsync("library");
        _settingServiceMock
            .Setup(s => s.SetSetting(SettingKeys.Automation.TranslationCycle, "custom"))
            .ReturnsAsync(true);

        var libraryMovie = CreateMovie(1, "Library 1");
        var customItem = new CustomMediaItem
        {
            Id = 61,
            CustomSourceId = 6,
            CustomSource = new CustomSource
            {
                Id = 6,
                Name = "Custom Source",
                SourceType = CustomSourceType.MovieRoot,
                RootPath = @"C:\custom",
                Recursive = true,
                Enabled = true,
                IncludeInAutomation = true
            },
            ItemKind = CustomMediaItemKind.Movie,
            Title = "Custom Single Budget",
            FileName = "custom-single.mkv",
            Path = @"C:\custom\custom-single.mkv",
            RelativePath = "custom-single.mkv",
            DateAdded = DateTime.UtcNow.AddDays(-3),
            TranslationState = TranslationState.Complete
        };

        _mediaStateServiceMock
            .Setup(m => m.GetMediaNeedingTranslationAsync(It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync([(libraryMovie, MediaType.Movie)]);
        _customMediaStateServiceMock
            .Setup(m => m.GetItemsNeedingTranslationAsync(It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync([customItem]);

        var invocationOrder = new List<string>();
        _automationServiceMock
            .Setup(a => a.ProcessLoadedMediaForAutomationAsync(
                customItem,
                MediaType.Movie,
                "fallback_schedule",
                true,
                false))
            .Callback(() => invocationOrder.Add("custom"))
            .ReturnsAsync(1);
        _automationServiceMock
            .Setup(a => a.ProcessLoadedMediaForAutomationAsync(
                libraryMovie,
                MediaType.Movie,
                "fallback_schedule",
                true,
                false))
            .Callback(() => invocationOrder.Add("library"))
            .ReturnsAsync(1);

        await _job.Execute();

        Assert.Equal(new[] { "library" }, invocationOrder);
        _settingServiceMock.Verify(
            s => s.SetSetting(SettingKeys.Automation.TranslationCycle, "custom"),
            Times.Once);
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
