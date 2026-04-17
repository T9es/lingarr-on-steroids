using System;
using System.IO;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Jobs;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Subtitle;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Jobs;

public class TranslationJobTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LingarrDbContext _dbContext;
    private readonly Mock<ISettingService> _settingServiceMock;
    private readonly TranslationJob _job;
    private readonly string _tempDirectory;

    public TranslationJobTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new LingarrDbContext(options);
        _dbContext.Database.EnsureCreated();

        _settingServiceMock = new Mock<ISettingService>();

        var subtitleService = new SubtitleService(NullLogger<SubtitleService>.Instance);
        var extractionService = new SubtitleExtractionService(
            NullLogger<SubtitleExtractionService>.Instance,
            _dbContext,
            _settingServiceMock.Object);

        _job = new TranslationJob(
            NullLogger<TranslationJob>.Instance,
            _settingServiceMock.Object,
            _dbContext,
            Mock.Of<IProgressService>(),
            subtitleService,
            Mock.Of<IScheduleService>(),
            Mock.Of<IStatisticsService>(),
            Mock.Of<ITranslationServiceFactory>(),
            Mock.Of<ITranslationRequestService>(),
            Mock.Of<IBatchFallbackService>(),
            extractionService,
            Mock.Of<ITranslationCancellationService>(),
            Mock.Of<IMediaStateService>(),
            Mock.Of<IDeferredRepairService>(),
            Mock.Of<IDashboardService>(),
            Mock.Of<ISourceSubtitleSnapshotService>());

        _tempDirectory = Path.Combine(Path.GetTempPath(), "lingarr-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task CleanupTemporaryExtractedSubtitleAsync_DeletesFileAndClearsMetadata()
    {
        var movie = CreateMovie(1);
        var subtitlePath = Path.Combine(_tempDirectory, "movie.eng.srt");
        await File.WriteAllTextAsync(
            subtitlePath,
            $"{SubtitleExtractionService.ExtractionMarkerPrefix} StreamIndex=0, Entries=1{Environment.NewLine}{Environment.NewLine}" +
            "1\n00:00:01,000 --> 00:00:02,000\nHello\n");

        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 0,
            Language = "eng",
            CodecName = "subrip",
            IsTextBased = true,
            IsExtracted = true,
            ExtractedPath = subtitlePath
        });

        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var request = new TranslationRequest
        {
            MediaId = movie.Id,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending
        };

        await _job.CleanupTemporaryExtractedSubtitleAsync(request, subtitlePath);

        Assert.False(File.Exists(subtitlePath));

        var embeddedSubtitle = await _dbContext.EmbeddedSubtitles.SingleAsync();
        Assert.False(embeddedSubtitle.IsExtracted);
        Assert.Null(embeddedSubtitle.ExtractedPath);
    }

    [Fact]
    public async Task CleanupTemporaryExtractedSubtitleAsync_ClearsMetadataWhenFileIsAlreadyMissing()
    {
        var movie = CreateMovie(2);
        var subtitlePath = Path.Combine(_tempDirectory, "missing.eng.srt");

        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 1,
            Language = "eng",
            CodecName = "subrip",
            IsTextBased = true,
            IsExtracted = true,
            ExtractedPath = subtitlePath
        });

        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var request = new TranslationRequest
        {
            MediaId = movie.Id,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending
        };

        await _job.CleanupTemporaryExtractedSubtitleAsync(request, subtitlePath);

        var embeddedSubtitle = await _dbContext.EmbeddedSubtitles.SingleAsync(es => es.MovieId == movie.Id);
        Assert.False(embeddedSubtitle.IsExtracted);
        Assert.Null(embeddedSubtitle.ExtractedPath);
    }

    [Fact]
    public async Task CleanSourceSubtitleFile_PreservesExtractionMarkerWhenRewriting()
    {
        var subtitlePath = Path.Combine(_tempDirectory, "clean-source.eng.srt");
        await File.WriteAllTextAsync(
            subtitlePath,
            $"{SubtitleExtractionService.ExtractionMarkerPrefix} StreamIndex=0, Entries=2{Environment.NewLine}{Environment.NewLine}" +
            "1\n00:00:01,000 --> 00:00:02,000\nm 185.89 20.59 b 193.53 20.98 203.71 21.49\n\n" +
            "2\n00:00:03,000 --> 00:00:04,000\nRegular dialogue\n");

        await _job.CleanSourceSubtitleFile(subtitlePath, stripSubtitleFormatting: false);

        Assert.True(SubtitleExtractionService.IsLingarrExtracted(subtitlePath));

        var content = await File.ReadAllTextAsync(subtitlePath);
        Assert.DoesNotContain("m 185.89 20.59", content);
        Assert.Contains("Regular dialogue", content);
    }

    [Fact]
    public async Task ShouldUseEmbeddedSourceSubtitle_ReturnsFalse_ForExternalSubtitlePath()
    {
        var subtitlePath = Path.Combine(_tempDirectory, "external-source.en.srt");
        await File.WriteAllTextAsync(
            subtitlePath,
            "1\n00:00:01,000 --> 00:00:02,000\nHello\n");

        var selectedSubtitle = new EmbeddedSubtitle
        {
            StreamIndex = 0,
            Language = "eng",
            CodecName = "subrip",
            IsTextBased = true
        };

        var result = TranslationJob.ShouldUseEmbeddedSourceSubtitle(subtitlePath, selectedSubtitle);

        Assert.False(result);
    }

    [Fact]
    public async Task ShouldUseEmbeddedSourceSubtitle_ReturnsTrue_ForLingarrExtractedSubtitlePath()
    {
        var subtitlePath = Path.Combine(_tempDirectory, "embedded-source.eng.srt");
        await File.WriteAllTextAsync(
            subtitlePath,
            $"{SubtitleExtractionService.ExtractionMarkerPrefix} StreamIndex=0, Entries=1{Environment.NewLine}{Environment.NewLine}" +
            "1\n00:00:01,000 --> 00:00:02,000\nHello\n");

        var selectedSubtitle = new EmbeddedSubtitle
        {
            StreamIndex = 0,
            Language = "eng",
            CodecName = "subrip",
            IsTextBased = true
        };

        var result = TranslationJob.ShouldUseEmbeddedSourceSubtitle(subtitlePath, selectedSubtitle);

        Assert.True(result);
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

    private static Movie CreateMovie(int id) => new()
    {
        Id = id,
        RadarrId = id,
        Title = $"Movie {id}",
        FileName = $"movie-{id}.mkv",
        Path = "/movies",
        DateAdded = DateTime.UtcNow
    };
}
