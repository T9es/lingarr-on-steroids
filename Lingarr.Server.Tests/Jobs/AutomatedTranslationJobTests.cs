using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Jobs;

/// <summary>
/// Tests for the redesigned AutomatedTranslationJob.
/// The new job delegates media selection to IMediaStateService.
/// </summary>
public class AutomatedTranslationJobTests : IDisposable
{
    private readonly LingarrDbContext _dbContext;
    private readonly Mock<IMediaSubtitleProcessor> _processorMock;
    private readonly Mock<IScheduleService> _scheduleServiceMock;
    private readonly Mock<ISettingService> _settingServiceMock;
    private readonly Mock<IMediaStateService> _mediaStateServiceMock;
    private readonly AutomatedTranslationJob _job;

    public AutomatedTranslationJobTests()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new LingarrDbContext(options);

        _processorMock = new Mock<IMediaSubtitleProcessor>();
        _scheduleServiceMock = new Mock<IScheduleService>();
        _settingServiceMock = new Mock<ISettingService>();
        _mediaStateServiceMock = new Mock<IMediaStateService>();

        _job = new AutomatedTranslationJob(
            _dbContext,
            NullLogger<AutomatedTranslationJob>.Instance,
            _processorMock.Object,
            _scheduleServiceMock.Object,
            _settingServiceMock.Object,
            _mediaStateServiceMock.Object);

        // Default settings setup
        _settingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Automation.AutomationEnabled))
            .ReturnsAsync("true");

        _settingServiceMock
            .Setup(s => s.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                { SettingKeys.Automation.MaxTranslationsPerRun, "10" },
                { SettingKeys.Automation.MovieAgeThreshold, "0" },
                { SettingKeys.Automation.ShowAgeThreshold, "0" }
            });
    }

    [Fact]
    public async Task Execute_WhenAutomationDisabled_SkipsProcessing()
    {
        // Arrange
        _settingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Automation.AutomationEnabled))
            .ReturnsAsync("false");

        // Act
        await _job.Execute();

        // Assert - should not call GetMediaNeedingTranslationAsync
        _mediaStateServiceMock.Verify(
            m => m.GetMediaNeedingTranslationAsync(It.IsAny<int>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task Execute_WhenAutomationEnabled_QueriesMediaStateService()
    {
        // Arrange
        _mediaStateServiceMock
            .Setup(m => m.GetMediaNeedingTranslationAsync(It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(new List<(IMedia, MediaType)>());

        // Act
        await _job.Execute();

        // Assert
        _mediaStateServiceMock.Verify(
            m => m.GetMediaNeedingTranslationAsync(It.IsAny<int>(), It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task Execute_WithPendingMedia_ProcessesThem()
    {
        // Arrange
        var movie = new Movie
        {
            Id = 1,
            RadarrId = 1,
            Title = "Test Movie",
            Path = "/test/path",
            FileName = "test",
            DateAdded = DateTime.UtcNow.AddDays(-7),
            TranslationState = TranslationState.Pending
        };
        
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        _mediaStateServiceMock
            .Setup(m => m.GetMediaNeedingTranslationAsync(It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(new List<(IMedia, MediaType)> { (movie, MediaType.Movie) });

        _processorMock
            .Setup(p => p.ProcessMediaForceAsync(
                It.IsAny<IMedia>(), 
                It.IsAny<MediaType>(), 
                It.IsAny<bool>(), 
                It.IsAny<bool>(),
                It.IsAny<bool>()))
            .ReturnsAsync(1);

        // Act
        await _job.Execute();

        // Assert
        _processorMock.Verify(
            p => p.ProcessMediaForceAsync(movie, MediaType.Movie, false, false, false),
            Times.Once);
    }

    [Fact]
    public async Task Execute_WithStaleMedia_RefreshesState()
    {
        // Arrange
        var movie = new Movie
        {
            Id = 1,
            RadarrId = 1,
            Title = "Stale Movie",
            Path = "/test/path",
            FileName = "test",
            DateAdded = DateTime.UtcNow.AddDays(-7),
            TranslationState = TranslationState.Stale
        };
        
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        _mediaStateServiceMock
            .Setup(m => m.GetMediaNeedingTranslationAsync(It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(new List<(IMedia, MediaType)> { (movie, MediaType.Movie) });

        _mediaStateServiceMock
            .Setup(m => m.UpdateStateAsync(It.IsAny<IMedia>(), It.IsAny<MediaType>(), It.IsAny<bool>()))
            .ReturnsAsync(TranslationState.Pending);

        _processorMock
            .Setup(p => p.ProcessMediaForceAsync(
                It.IsAny<IMedia>(), 
                It.IsAny<MediaType>(), 
                It.IsAny<bool>(), 
                It.IsAny<bool>(),
                It.IsAny<bool>()))
            .ReturnsAsync(1);

        // Act
        await _job.Execute();

        // Assert - should refresh state before processing
        _mediaStateServiceMock.Verify(
            m => m.UpdateStateAsync(movie, MediaType.Movie, It.IsAny<bool>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task Execute_WithStaleMediaThatBecomesComplete_SkipsProcessing()
    {
        // Arrange
        var movie = new Movie
        {
            Id = 1,
            RadarrId = 1,
            Title = "Stale But Complete",
            Path = "/test/path",
            FileName = "test",
            DateAdded = DateTime.UtcNow.AddDays(-7),
            TranslationState = TranslationState.Stale
        };
        
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        _mediaStateServiceMock
            .Setup(m => m.GetMediaNeedingTranslationAsync(It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(new List<(IMedia, MediaType)> { (movie, MediaType.Movie) });

        // Refreshing state shows it's actually complete
        _mediaStateServiceMock
            .Setup(m => m.UpdateStateAsync(It.IsAny<IMedia>(), It.IsAny<MediaType>(), It.IsAny<bool>()))
            .ReturnsAsync(TranslationState.Complete);

        // Act
        await _job.Execute();

        // Assert - should NOT process since it's complete
        _processorMock.Verify(
            p => p.ProcessMediaForceAsync(
                It.IsAny<IMedia>(), 
                It.IsAny<MediaType>(), 
                It.IsAny<bool>(), 
                It.IsAny<bool>(),
                It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task Execute_RespectsMaxTranslationsPerRun()
    {
        // Arrange
        _settingServiceMock
            .Setup(s => s.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                { SettingKeys.Automation.MaxTranslationsPerRun, "2" },
                { SettingKeys.Automation.MovieAgeThreshold, "0" },
                { SettingKeys.Automation.ShowAgeThreshold, "0" }
            });

        var movies = new List<(IMedia, MediaType)>();
        for (int i = 1; i <= 5; i++)
        {
            var movie = new Movie
            {
                Id = i,
                RadarrId = i,
                Title = $"Movie {i}",
                Path = "/test/path",
                FileName = $"movie{i}",
                DateAdded = DateTime.UtcNow.AddDays(-7),
                TranslationState = TranslationState.Pending
            };
            _dbContext.Movies.Add(movie);
            movies.Add((movie, MediaType.Movie));
        }
        await _dbContext.SaveChangesAsync();

        _mediaStateServiceMock
            .Setup(m => m.GetMediaNeedingTranslationAsync(It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(movies);

        _processorMock
            .Setup(p => p.ProcessMediaForceAsync(
                It.IsAny<IMedia>(), 
                It.IsAny<MediaType>(), 
                It.IsAny<bool>(), 
                It.IsAny<bool>(),
                It.IsAny<bool>()))
            .ReturnsAsync(1);

        // Act
        await _job.Execute();

        // Assert - should only process 2 (max per run)
        _processorMock.Verify(
            p => p.ProcessMediaForceAsync(
                It.IsAny<IMedia>(), 
                It.IsAny<MediaType>(), 
                It.IsAny<bool>(), 
                It.IsAny<bool>(),
                It.IsAny<bool>()),
            Times.Exactly(2));
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
        GC.SuppressFinalize(this);
    }
}
