using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Jobs;
using Lingarr.Server.Services.Subtitle;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Jobs;

public class SubtitleLanguageContradictionJobTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LingarrDbContext _dbContext;
    private readonly string _tempDirectory;
    private readonly Mock<ISettingService> _settingServiceMock = new();
    private readonly Mock<ISubtitleLanguageContradictionDetector> _detectorMock = new();
    private readonly Mock<IMediaSubtitleProcessor> _processorMock = new();
    private readonly SubtitleLanguageContradictionJob _job;
    private readonly List<int> _checkedMovieIds = new();
    private Func<IReadOnlyList<SubtitleLanguageCheckRequest>, Task<IReadOnlyList<SubtitleLanguageCheckOutcome>>>
        _checkBehavior;

    public SubtitleLanguageContradictionJobTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbContext = new LingarrDbContext(options);
        _dbContext.Database.EnsureCreated();

        _tempDirectory = Path.Combine(Path.GetTempPath(), "lingarr-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        _settingServiceMock
            .Setup(service => service.GetSetting(It.IsAny<string>()))
            .ReturnsAsync((string key) => key switch
            {
                SettingKeys.Automation.AutomationEnabled => "true",
                SettingKeys.Maintenance.SubtitleLanguageCheckMaxItems => "2",
                _ => string.Empty
            });

        _detectorMock
            .SetupGet(detector => detector.CurrentVersion)
            .Returns(SubtitleLanguageContradictionDetector.DetectionVersion);
        _detectorMock
            .Setup(detector => detector.CheckBatchAsync(
                It.IsAny<IReadOnlyList<SubtitleLanguageCheckRequest>>(),
                It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<SubtitleLanguageCheckRequest> requests, CancellationToken _) =>
            {
                _checkedMovieIds.AddRange(requests
                    .Select(request => request.Subtitle.MovieId.GetValueOrDefault())
                    .Where(movieId => movieId > 0));
                return _checkBehavior(requests);
            });

        _processorMock
            .Setup(processor => processor.ProcessMediaForceAsync(
                It.IsAny<IMedia>(),
                It.IsAny<MediaType>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<int?>(),
                It.IsAny<bool>()))
            .ReturnsAsync(1);

        _checkBehavior = StampNegativeAsync;

        _job = new SubtitleLanguageContradictionJob(
            _dbContext,
            _settingServiceMock.Object,
            _detectorMock.Object,
            _processorMock.Object,
            NullLogger<SubtitleLanguageContradictionJob>.Instance);
    }

    [Fact]
    public async Task Execute_WithBacklogLargerThanCap_ReachesDifferentRowsOnLaterRuns()
    {
        AddMovieWithSubtitle(1);
        AddMovieWithSubtitle(2);
        AddMovieWithSubtitle(3);
        await _dbContext.SaveChangesAsync();

        await _job.Execute();
        var firstRun = _checkedMovieIds.Distinct().ToList();
        _checkedMovieIds.Clear();

        await _job.Execute();
        var secondRun = _checkedMovieIds.Distinct().ToList();
        _checkedMovieIds.Clear();

        await _job.Execute();
        var thirdRun = _checkedMovieIds.Distinct().ToList();
        _checkedMovieIds.Clear();

        await _job.Execute();
        var fourthRun = _checkedMovieIds.Distinct().ToList();

        // The cap is two media per run, but with only one movie slot per run the backlog must
        // still advance instead of rechecking the same oldest row every night.
        Assert.Single(firstRun);
        Assert.Single(secondRun);
        Assert.Single(thirdRun);
        Assert.Empty(firstRun.Intersect(secondRun));
        Assert.Empty(secondRun.Intersect(thirdRun));
        Assert.Empty(firstRun.Intersect(thirdRun));
        Assert.Equal(new[] { 1, 2, 3 }, firstRun.Concat(secondRun).Concat(thirdRun).OrderBy(id => id));
        Assert.Empty(fourthRun);
    }

    [Fact]
    public async Task Execute_WhenContradictionIsUnchanged_DoesNotRequeueAgain()
    {
        var movie = AddMovieWithSubtitle(11);
        var subtitle = movie.EmbeddedSubtitles[0];
        subtitle.Language = "jpn";
        subtitle.DetectedLanguage = "en";
        subtitle.DetectedLanguageConfidence = 0.96;
        subtitle.LanguageDetectionMethod = "script-ratio+stopwords";
        subtitle.LanguageCheckedAt = DateTime.UtcNow.AddDays(-8);
        subtitle.LanguageDetectionVersion = SubtitleLanguageContradictionDetector.DetectionVersion;
        await _dbContext.SaveChangesAsync();

        _checkBehavior = StampContradictionAsync;

        await _job.Execute();

        _processorMock.Verify(
            processor => processor.ProcessMediaForceAsync(
                It.IsAny<IMedia>(),
                It.IsAny<MediaType>(),
                true,
                false,
                false,
                true,
                null,
                true),
            Times.Never);
    }

    [Fact]
    public async Task Execute_WhenContradictionIsNew_RequeuesOnce()
    {
        var movie = AddMovieWithSubtitle(21);
        var subtitle = movie.EmbeddedSubtitles[0];
        subtitle.Language = "jpn";
        subtitle.LanguageDetectionMethod = "no-contradiction";
        subtitle.LanguageCheckedAt = DateTime.UtcNow.AddHours(-7);
        subtitle.LanguageDetectionVersion = SubtitleLanguageContradictionDetector.DetectionVersion;
        await _dbContext.SaveChangesAsync();

        _checkBehavior = StampContradictionAsync;

        await _job.Execute();

        _processorMock.Verify(
            processor => processor.ProcessMediaForceAsync(
                It.IsAny<IMedia>(),
                It.IsAny<MediaType>(),
                true,
                false,
                false,
                true,
                null,
                true),
            Times.Once);
    }

    [Fact]
    public async Task Execute_WhenContradictionConfidenceIsBelowFloor_DoesNotRequeue()
    {
        var movie = AddMovieWithSubtitle(31);
        var subtitle = movie.EmbeddedSubtitles[0];
        subtitle.Language = "jpn";
        subtitle.LanguageDetectionMethod = "no-contradiction";
        subtitle.LanguageCheckedAt = DateTime.UtcNow.AddHours(-7);
        subtitle.LanguageDetectionVersion = SubtitleLanguageContradictionDetector.DetectionVersion;
        await _dbContext.SaveChangesAsync();

        _checkBehavior = StampLowConfidenceContradictionAsync;

        await _job.Execute();

        _processorMock.Verify(
            processor => processor.ProcessMediaForceAsync(
                It.IsAny<IMedia>(),
                It.IsAny<MediaType>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<int?>(),
                It.IsAny<bool>()),
            Times.Never);
    }

    [Theory]
    [InlineData("en", "jpn", "en", false)]
    [InlineData("ja", "jpn", "en", true)]
    [InlineData("ru", "jpn", "uk", true)]
    [InlineData(null, "jpn", "en", true)]
    public void ShouldRequeueAfterAdoption_OnlyWhenTheAdoptedLanguageChanged(
        string? previousEffectiveLanguage,
        string declaredLanguage,
        string currentEffectiveLanguage,
        bool expected)
    {
        var result = SubtitleLanguageContradictionJob.ShouldRequeueAfterAdoption(
            previousEffectiveLanguage,
            declaredLanguage,
            currentEffectiveLanguage);

        Assert.Equal(expected, result);
    }

    private Movie AddMovieWithSubtitle(int id)
    {
        var fileName = $"movie-{id}.mkv";
        File.WriteAllText(Path.Combine(_tempDirectory, fileName), "media");
        var movie = new Movie
        {
            Id = id,
            RadarrId = id,
            Title = $"Movie {id}",
            FileName = fileName,
            Path = _tempDirectory,
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.Pending
        };
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 0,
            Language = "eng",
            Title = "Full",
            CodecName = "ass",
            IsTextBased = true
        });
        _dbContext.Movies.Add(movie);
        return movie;
    }

    private async Task<IReadOnlyList<SubtitleLanguageCheckOutcome>> StampNegativeAsync(
        IReadOnlyList<SubtitleLanguageCheckRequest> requests)
    {
        var outcomes = new List<SubtitleLanguageCheckOutcome>(requests.Count);
        foreach (var request in requests)
        {
            var subtitle = request.Subtitle;
            subtitle.DetectedLanguage = null;
            subtitle.DetectedLanguageConfidence = null;
            subtitle.LanguageDetectionMethod = "no-contradiction";
            subtitle.LanguageDetectionEvidence = "checked by test";
            subtitle.LanguageCheckedAt = DateTime.UtcNow;
            subtitle.LanguageDetectionVersion = SubtitleLanguageContradictionDetector.DetectionVersion;
            outcomes.Add(new SubtitleLanguageCheckOutcome(
                subtitle,
                SubtitleLanguageVerdict.Consistent,
                subtitle.Language,
                null,
                0,
                "no-contradiction",
                "checked by test",
                Persisted: true));
        }

        await _dbContext.SaveChangesAsync();
        return outcomes;
    }

    private async Task<IReadOnlyList<SubtitleLanguageCheckOutcome>> StampContradictionAsync(
        IReadOnlyList<SubtitleLanguageCheckRequest> requests)
    {
        var outcomes = new List<SubtitleLanguageCheckOutcome>(requests.Count);
        foreach (var request in requests)
        {
            var subtitle = request.Subtitle;
            subtitle.DetectedLanguage = "en";
            subtitle.DetectedLanguageConfidence = 0.97;
            subtitle.LanguageDetectionMethod = "script-ratio+stopwords";
            subtitle.LanguageDetectionEvidence = "checked by test";
            subtitle.LanguageCheckedAt = DateTime.UtcNow;
            subtitle.LanguageDetectionVersion = SubtitleLanguageContradictionDetector.DetectionVersion;
            outcomes.Add(new SubtitleLanguageCheckOutcome(
                subtitle,
                SubtitleLanguageVerdict.Contradiction,
                subtitle.Language,
                "en",
                0.97,
                "script-ratio+stopwords",
                "checked by test",
                Persisted: true));
        }

        await _dbContext.SaveChangesAsync();
        return outcomes;
    }

    private async Task<IReadOnlyList<SubtitleLanguageCheckOutcome>> StampLowConfidenceContradictionAsync(
        IReadOnlyList<SubtitleLanguageCheckRequest> requests)
    {
        var outcomes = new List<SubtitleLanguageCheckOutcome>(requests.Count);
        foreach (var request in requests)
        {
            var subtitle = request.Subtitle;
            subtitle.DetectedLanguage = "en";
            subtitle.DetectedLanguageConfidence = 0.89;
            subtitle.LanguageDetectionMethod = "script-ratio+stopwords";
            subtitle.LanguageDetectionEvidence = "checked by test";
            subtitle.LanguageCheckedAt = DateTime.UtcNow;
            subtitle.LanguageDetectionVersion = SubtitleLanguageContradictionDetector.DetectionVersion;
            outcomes.Add(new SubtitleLanguageCheckOutcome(
                subtitle,
                SubtitleLanguageVerdict.Contradiction,
                subtitle.Language,
                "en",
                0.89,
                "script-ratio+stopwords",
                "checked by test",
                Persisted: true));
        }

        await _dbContext.SaveChangesAsync();
        return outcomes;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Close();
        _connection.Dispose();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
