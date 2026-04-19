using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Subtitle;
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
            Mock.Of<ICustomMediaStateService>(),
            Mock.Of<IDeferredRepairService>(),
            Mock.Of<IDashboardService>(),
            Mock.Of<ISourceSubtitleSnapshotService>(),
            Mock.Of<IUploadWorkspaceService>());

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

    [Fact]
    public async Task GetPreExistingExtractedSubtitlePathsAsync_FindsExistingAssExtraction()
    {
        var movie = CreateMovie(3);
        var subtitlePath = Path.Combine(_tempDirectory, "movie-3.eng.ass");
        await File.WriteAllTextAsync(
            subtitlePath,
            $"{SubtitleExtractionService.ExtractionMarkerPrefix} StreamIndex=0, Entries=1{Environment.NewLine}{Environment.NewLine}" +
            "Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,{\\an7}Hello");

        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 0,
            Language = "eng",
            CodecName = "ass",
            IsTextBased = true,
            IsExtracted = true,
            ExtractedPath = subtitlePath
        });

        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var existingPaths = await _job.GetPreExistingExtractedSubtitlePathsAsync(
            movie.Id,
            MediaType.Movie,
            "en");

        Assert.Contains(subtitlePath, existingPaths);
        Assert.True(TranslationJob.IsPreExistingExtractionPath(subtitlePath, existingPaths));
        Assert.DoesNotContain(Path.Combine(_tempDirectory, "movie-3.en.srt"), existingPaths);
    }

    [Fact]
    public async Task ExecuteAsync_FallbackPreservesPreExistingExtractedAssFile_WhenInitialSourceIsEmpty()
    {
        var sourceSubtitlePath = Path.Combine(_tempDirectory, "movie-4.external.en.srt");
        await File.WriteAllTextAsync(sourceSubtitlePath, string.Empty);

        var extractedAssPath = Path.Combine(_tempDirectory, "movie-4.eng.ass");
        await File.WriteAllTextAsync(
            extractedAssPath,
            $"{SubtitleExtractionService.ExtractionMarkerPrefix} StreamIndex=2, Entries=1{Environment.NewLine}{Environment.NewLine}" +
            "Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,Hello");

        var movie = CreateMovie(4);
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 2,
            Language = "eng",
            CodecName = "ass",
            IsTextBased = true,
            IsExtracted = true,
            ExtractedPath = extractedAssPath
        });

        var request = new TranslationRequest
        {
            MediaId = movie.Id,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = sourceSubtitlePath
        };

        _dbContext.Movies.Add(movie);
        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var settingServiceMock = new Mock<ISettingService>();
        settingServiceMock
            .Setup(service => service.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> keys) =>
            {
                var settings = new Dictionary<string, string>();
                foreach (var key in keys)
                {
                    settings[key] = string.Empty;
                }

                settings[SettingKeys.SubtitleValidation.ValidateSubtitles] = "false";
                settings[SettingKeys.Translation.UseBatchTranslation] = "false";
                return settings;
            });

        var subtitleServiceMock = new Mock<ISubtitleService>();
        subtitleServiceMock
            .Setup(service => service.ReadSubtitles(sourceSubtitlePath))
            .ReturnsAsync([]);
        subtitleServiceMock
            .Setup(service => service.ReadSubtitles(extractedAssPath))
            .ReturnsAsync([
                new SubtitleItem
                {
                    Position = 1,
                    StartTime = 1000,
                    EndTime = 2000,
                    Lines = ["Hello"],
                    PlaintextLines = ["Hello"],
                    TranslatedLines = []
                }
            ]);

        var extractionServiceMock = new Mock<ISubtitleExtractionService>();
        extractionServiceMock
            .Setup(service => service.TryExtractEmbeddedSubtitle(
                request.MediaId!.Value,
                request.MediaType,
                request.SourceLanguage,
                It.IsAny<List<int>?>(),
                It.IsAny<int?>()))
            .ReturnsAsync(extractedAssPath);
        extractionServiceMock
            .Setup(service => service.ClearExtractionMetadataAsync(It.IsAny<int>(), It.IsAny<MediaType>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var translationServiceMock = new Mock<ITranslationService>();
        translationServiceMock
            .Setup(service => service.TranslateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TranslationException("forced-test-failure"));

        var translationServiceFactoryMock = new Mock<ITranslationServiceFactory>();
        translationServiceFactoryMock
            .Setup(factory => factory.CreateTranslationService(It.IsAny<string>()))
            .Returns(translationServiceMock.Object);

        var translationRequestServiceMock = new Mock<ITranslationRequestService>();
        translationRequestServiceMock
            .Setup(service => service.UpdateTranslationRequest(
                It.IsAny<TranslationRequest>(),
                It.IsAny<TranslationStatus>(),
                It.IsAny<string?>()))
            .ReturnsAsync((TranslationRequest value, TranslationStatus _, string? _) => value);
        translationRequestServiceMock
            .Setup(service => service.ClearMediaHash(It.IsAny<TranslationRequest>()))
            .Returns(Task.CompletedTask);
        translationRequestServiceMock
            .Setup(service => service.UpdateActiveCount())
            .ReturnsAsync(0);

        var cancellationServiceMock = new Mock<ITranslationCancellationService>();
        cancellationServiceMock
            .Setup(service => service.RegisterJob(It.IsAny<int>()))
            .Returns(CancellationToken.None);

        var progressServiceMock = new Mock<IProgressService>();
        progressServiceMock
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var mediaStateServiceMock = new Mock<IMediaStateService>();
        mediaStateServiceMock
            .Setup(service => service.UpdateStateAsync(It.IsAny<Lingarr.Core.Interfaces.IMedia>(), It.IsAny<MediaType>(), It.IsAny<bool>()))
            .ReturnsAsync(TranslationState.Unknown);

        var dashboardServiceMock = new Mock<IDashboardService>();
        dashboardServiceMock
            .Setup(service => service.LogError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var sourceSnapshotServiceMock = new Mock<ISourceSubtitleSnapshotService>();
        sourceSnapshotServiceMock
            .Setup(service => service.CreateEmbeddedSnapshot(It.IsAny<EmbeddedSubtitle>(), It.IsAny<string>()))
            .Returns(new SourceSubtitleSnapshot
            {
                Version = SourceSubtitleSnapshot.CurrentVersion,
                SourceType = SourceSubtitleSnapshot.EmbeddedType,
                SourceLanguage = "en",
                Identity = "embedded:2",
                Fingerprint = "fingerprint-embedded",
                StreamIndex = 2
            });
        sourceSnapshotServiceMock
            .Setup(service => service.CreateExternalSnapshot(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new SourceSubtitleSnapshot
            {
                Version = SourceSubtitleSnapshot.CurrentVersion,
                SourceType = SourceSubtitleSnapshot.ExternalType,
                SourceLanguage = "en",
                Identity = "external",
                Fingerprint = "fingerprint-external",
                FileSizeBytes = 0
            });

        var job = new TranslationJob(
            NullLogger<TranslationJob>.Instance,
            settingServiceMock.Object,
            _dbContext,
            progressServiceMock.Object,
            subtitleServiceMock.Object,
            Mock.Of<IScheduleService>(),
            Mock.Of<IStatisticsService>(),
            translationServiceFactoryMock.Object,
            translationRequestServiceMock.Object,
            Mock.Of<IBatchFallbackService>(),
            extractionServiceMock.Object,
            cancellationServiceMock.Object,
            mediaStateServiceMock.Object,
            Mock.Of<ICustomMediaStateService>(),
            Mock.Of<IDeferredRepairService>(),
            dashboardServiceMock.Object,
            sourceSnapshotServiceMock.Object,
            Mock.Of<IUploadWorkspaceService>());

        await Assert.ThrowsAsync<TranslationException>(() => job.ExecuteAsync(request.Id, CancellationToken.None));

        Assert.True(File.Exists(extractedAssPath));
        var updatedRequest = await _dbContext.TranslationRequests.SingleAsync(item => item.Id == request.Id);
        Assert.Equal(extractedAssPath, updatedRequest.SubtitleToTranslate);
        Assert.Equal(".ass", updatedRequest.SourceSubtitleFormat);
        Assert.Equal(".ass", updatedRequest.RequiredOutputFormats);
        Assert.Equal("match-source", updatedRequest.SubtitleOutputMode);
        extractionServiceMock.Verify(service => service.TryExtractEmbeddedSubtitle(
            request.MediaId!.Value,
            request.MediaType,
            request.SourceLanguage,
            It.IsAny<List<int>?>(),
            It.IsAny<int?>()), Times.Once);
        extractionServiceMock.Verify(service => service.ClearExtractionMetadataAsync(
            It.IsAny<int>(),
            It.IsAny<MediaType>(),
            It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_FallbackMarksNewlyExtractedFileAsTemporary_WhenMetadataIsPersistedDuringExtraction()
    {
        var sourceSubtitlePath = Path.Combine(_tempDirectory, "movie-5.external.en.srt");
        await File.WriteAllTextAsync(sourceSubtitlePath, string.Empty);
        var extractedAssPath = Path.Combine(_tempDirectory, "movie-5.eng.ass");

        var movie = CreateMovie(5);
        var fallbackSubtitle = new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 3,
            Language = "eng",
            CodecName = "ass",
            IsTextBased = true,
            IsExtracted = false
        };
        movie.EmbeddedSubtitles.Add(fallbackSubtitle);

        var request = new TranslationRequest
        {
            MediaId = movie.Id,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = sourceSubtitlePath
        };

        _dbContext.Movies.Add(movie);
        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var settingServiceMock = new Mock<ISettingService>();
        settingServiceMock
            .Setup(service => service.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> keys) =>
            {
                var settings = new Dictionary<string, string>();
                foreach (var key in keys)
                {
                    settings[key] = string.Empty;
                }

                settings[SettingKeys.SubtitleValidation.ValidateSubtitles] = "false";
                settings[SettingKeys.Translation.UseBatchTranslation] = "false";
                return settings;
            });

        var subtitleServiceMock = new Mock<ISubtitleService>();
        subtitleServiceMock
            .Setup(service => service.ReadSubtitles(sourceSubtitlePath))
            .ReturnsAsync([]);
        subtitleServiceMock
            .Setup(service => service.ReadSubtitles(extractedAssPath))
            .ReturnsAsync([
                new SubtitleItem
                {
                    Position = 1,
                    StartTime = 1000,
                    EndTime = 2000,
                    Lines = ["Hello"],
                    PlaintextLines = ["Hello"],
                    TranslatedLines = []
                }
            ]);

        var extractionServiceMock = new Mock<ISubtitleExtractionService>();
        extractionServiceMock
            .Setup(service => service.TryExtractEmbeddedSubtitle(
                request.MediaId!.Value,
                request.MediaType,
                request.SourceLanguage,
                It.IsAny<List<int>?>(),
                It.IsAny<int?>()))
            .Callback<int, MediaType, string, List<int>?, int?>((_, _, _, _, _) =>
            {
                File.WriteAllText(
                    extractedAssPath,
                    $"{SubtitleExtractionService.ExtractionMarkerPrefix} StreamIndex=3, Entries=1{Environment.NewLine}{Environment.NewLine}" +
                    "Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,Hello");

                fallbackSubtitle.IsExtracted = true;
                fallbackSubtitle.ExtractedPath = extractedAssPath;
                _dbContext.SaveChanges();
            })
            .ReturnsAsync(extractedAssPath);
        extractionServiceMock
            .Setup(service => service.ClearExtractionMetadataAsync(It.IsAny<int>(), It.IsAny<MediaType>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var translationServiceMock = new Mock<ITranslationService>();
        translationServiceMock
            .Setup(service => service.TranslateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TranslationException("forced-test-failure"));

        var translationServiceFactoryMock = new Mock<ITranslationServiceFactory>();
        translationServiceFactoryMock
            .Setup(factory => factory.CreateTranslationService(It.IsAny<string>()))
            .Returns(translationServiceMock.Object);

        var translationRequestServiceMock = new Mock<ITranslationRequestService>();
        translationRequestServiceMock
            .Setup(service => service.UpdateTranslationRequest(
                It.IsAny<TranslationRequest>(),
                It.IsAny<TranslationStatus>(),
                It.IsAny<string?>()))
            .ReturnsAsync((TranslationRequest value, TranslationStatus _, string? _) => value);
        translationRequestServiceMock
            .Setup(service => service.ClearMediaHash(It.IsAny<TranslationRequest>()))
            .Returns(Task.CompletedTask);
        translationRequestServiceMock
            .Setup(service => service.UpdateActiveCount())
            .ReturnsAsync(0);

        var cancellationServiceMock = new Mock<ITranslationCancellationService>();
        cancellationServiceMock
            .Setup(service => service.RegisterJob(It.IsAny<int>()))
            .Returns(CancellationToken.None);

        var progressServiceMock = new Mock<IProgressService>();
        progressServiceMock
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var mediaStateServiceMock = new Mock<IMediaStateService>();
        mediaStateServiceMock
            .Setup(service => service.UpdateStateAsync(It.IsAny<Lingarr.Core.Interfaces.IMedia>(), It.IsAny<MediaType>(), It.IsAny<bool>()))
            .ReturnsAsync(TranslationState.Unknown);

        var dashboardServiceMock = new Mock<IDashboardService>();
        dashboardServiceMock
            .Setup(service => service.LogError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var sourceSnapshotServiceMock = new Mock<ISourceSubtitleSnapshotService>();
        sourceSnapshotServiceMock
            .Setup(service => service.CreateEmbeddedSnapshot(It.IsAny<EmbeddedSubtitle>(), It.IsAny<string>()))
            .Returns(new SourceSubtitleSnapshot
            {
                Version = SourceSubtitleSnapshot.CurrentVersion,
                SourceType = SourceSubtitleSnapshot.EmbeddedType,
                SourceLanguage = "en",
                Identity = "embedded:3",
                Fingerprint = "fingerprint-embedded",
                StreamIndex = 3
            });
        sourceSnapshotServiceMock
            .Setup(service => service.CreateExternalSnapshot(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new SourceSubtitleSnapshot
            {
                Version = SourceSubtitleSnapshot.CurrentVersion,
                SourceType = SourceSubtitleSnapshot.ExternalType,
                SourceLanguage = "en",
                Identity = "external",
                Fingerprint = "fingerprint-external",
                FileSizeBytes = 0
            });

        var job = new TranslationJob(
            NullLogger<TranslationJob>.Instance,
            settingServiceMock.Object,
            _dbContext,
            progressServiceMock.Object,
            subtitleServiceMock.Object,
            Mock.Of<IScheduleService>(),
            Mock.Of<IStatisticsService>(),
            translationServiceFactoryMock.Object,
            translationRequestServiceMock.Object,
            Mock.Of<IBatchFallbackService>(),
            extractionServiceMock.Object,
            cancellationServiceMock.Object,
            mediaStateServiceMock.Object,
            Mock.Of<ICustomMediaStateService>(),
            Mock.Of<IDeferredRepairService>(),
            dashboardServiceMock.Object,
            sourceSnapshotServiceMock.Object,
            Mock.Of<IUploadWorkspaceService>());

        await Assert.ThrowsAsync<TranslationException>(() => job.ExecuteAsync(request.Id, CancellationToken.None));

        Assert.False(File.Exists(extractedAssPath));
        var updatedRequest = await _dbContext.TranslationRequests.SingleAsync(item => item.Id == request.Id);
        Assert.Equal(extractedAssPath, updatedRequest.SubtitleToTranslate);
        Assert.Equal(".ass", updatedRequest.SourceSubtitleFormat);
        Assert.Equal(".ass", updatedRequest.RequiredOutputFormats);
        Assert.Equal("match-source", updatedRequest.SubtitleOutputMode);
        extractionServiceMock.Verify(service => service.ClearExtractionMetadataAsync(
            request.MediaId!.Value,
            request.MediaType,
            extractedAssPath), Times.Once);
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
