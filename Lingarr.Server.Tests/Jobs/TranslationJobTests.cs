using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
using Lingarr.Server.Models;
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
    private readonly IEmbeddedSubtitleCacheService _embeddedSubtitleCacheService;
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

        _tempDirectory = Path.Combine(Path.GetTempPath(), "lingarr-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        _settingServiceMock = new Mock<ISettingService>();
        _embeddedSubtitleCacheService = new EmbeddedSubtitleCacheService(
            NullLogger<EmbeddedSubtitleCacheService>.Instance,
            Path.Combine(_tempDirectory, "embedded-subtitle-cache"),
            retention: null);

        var subtitleService = new SubtitleService(NullLogger<SubtitleService>.Instance);
        var extractionService = new SubtitleExtractionService(
            NullLogger<SubtitleExtractionService>.Instance,
            _dbContext,
            _settingServiceMock.Object,
            subtitleService,
            _embeddedSubtitleCacheService,
            Mock.Of<ISubtitleLanguageDetectionService>());

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
            Mock.Of<ISourceSubtitleResolver>(),
            _embeddedSubtitleCacheService,
            Mock.Of<IUploadWorkspaceService>());

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
    public async Task BuildOcrTranslationPromptContextAsync_ForEpisode_DoesNotFollowAutoIncludeCycles()
    {
        var show = new Show
        {
            Id = 3001,
            SonarrId = 3001,
            Title = "Ajin",
            Path = "/shows/ajin",
            DateAdded = DateTime.UtcNow
        };
        var season = new Season
        {
            Id = 3002,
            SeasonNumber = 1,
            Path = "/shows/ajin/season-1",
            ShowId = show.Id,
            Show = show
        };
        var episode = new Episode
        {
            Id = 3003,
            SonarrId = 3003,
            EpisodeNumber = 4,
            Title = "Have You Ever Seen a Black Ghost?",
            FileName = "ajin-s01e04.mkv",
            Path = "/shows/ajin/season-1",
            SeasonId = season.Id,
            Season = season
        };
        var subtitle = new EmbeddedSubtitle
        {
            EpisodeId = episode.Id,
            StreamIndex = 1,
            Language = "eng",
            CodecName = "hdmv_pgs_subtitle",
            Title = "Dialogue",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.Succeeded,
            OcrExtractedPath = "/app/config/embedded-subtitle-cache/episode-3003-stream-1-eng.ocr.srt"
        };

        show.Seasons.Add(season);
        season.Episodes.Add(episode);
        episode.EmbeddedSubtitles.Add(subtitle);
        _dbContext.Shows.Add(show);
        await _dbContext.SaveChangesAsync();

        var request = new TranslationRequest
        {
            MediaId = episode.Id,
            MediaType = MediaType.Episode,
            Title = episode.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SourceSubtitleType = "FullDialogue",
            SubtitleToTranslate = subtitle.OcrExtractedPath,
            Status = TranslationStatus.Pending
        };

        var context = await _job.BuildOcrTranslationPromptContextAsync(
            request,
            subtitle,
            CancellationToken.None);

        Assert.NotNull(context);
        Assert.Equal("Ajin", context.SeriesTitle);
        Assert.Equal(1, context.SeasonNumber);
        Assert.Equal(4, context.EpisodeNumber);
        Assert.Equal("Have You Ever Seen a Black Ghost?", context.EpisodeTitle);
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
    public async Task ShouldUseEmbeddedSourceSubtitle_ReturnsTrue_ForUsableOcrSubtitlePath()
    {
        var subtitlePath = Path.Combine(_tempDirectory, "movie.eng.ocr.srt");
        await File.WriteAllTextAsync(subtitlePath, "1\n00:00:01,000 --> 00:00:02,000\nAlr.\n");

        var selectedSubtitle = new EmbeddedSubtitle
        {
            StreamIndex = 0,
            Language = "eng",
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.Succeeded,
            OcrExtractedPath = subtitlePath
        };

        var result = TranslationJob.ShouldUseEmbeddedSourceSubtitle(subtitlePath, selectedSubtitle);

        Assert.True(result);
    }

    [Fact]
    public void EmbeddedSourceLanguageMismatchesRequest_ReturnsTrue_WhenSelectedStreamLanguageDiffersFromRequest()
    {
        var request = new TranslationRequest
        {
            SourceLanguage = "en",
            TargetLanguage = "pl",
            Title = "Wrong language source",
            MediaType = MediaType.Episode,
            Status = TranslationStatus.Pending
        };
        var selectedSubtitle = new EmbeddedSubtitle
        {
            StreamIndex = 1,
            Language = "pol",
            CodecName = "subrip",
            IsTextBased = true
        };

        var result = TranslationJob.EmbeddedSourceLanguageMismatchesRequest(request, selectedSubtitle);

        Assert.True(result);
    }

    [Fact]
    public void EmbeddedSourceLanguageMismatchesRequest_ReturnsFalse_WhenSelectedStreamMatchesRequest()
    {
        var request = new TranslationRequest
        {
            SourceLanguage = "en",
            TargetLanguage = "pl",
            Title = "Right language source",
            MediaType = MediaType.Episode,
            Status = TranslationStatus.Pending
        };
        var selectedSubtitle = new EmbeddedSubtitle
        {
            StreamIndex = 2,
            Language = "eng",
            CodecName = "subrip",
            IsTextBased = true
        };

        var result = TranslationJob.EmbeddedSourceLanguageMismatchesRequest(request, selectedSubtitle);

        Assert.False(result);
    }

    [Fact]
    public void GetUnsafeSourceCancellationReason_ReturnsReason_ForSparseNeutralEmbeddedPrimarySource()
    {
        var request = new TranslationRequest
        {
            SourceLanguage = "en",
            TargetLanguage = "pl",
            Title = "Sparse neutral source",
            MediaType = MediaType.Episode,
            Status = TranslationStatus.Pending,
            SourceSubtitleType = SubtitleLanguageHelper.TypeFull,
            SourceSubtitleEntryCount = 1,
            IsForcedSubtitle = false,
            SourceSubtitleFormat = ".srt"
        };
        var selectedSubtitle = new EmbeddedSubtitle
        {
            StreamIndex = 1,
            Language = "eng",
            Title = "English",
            CodecName = "subrip",
            IsTextBased = true,
            IsForced = false
        };
        var settings = new Dictionary<string, string>
        {
            [SettingKeys.Translation.TranslateSupplementalSubtitles] = "false"
        };

        var reason = TranslationJob.GetUnsafeSourceCancellationReason(
            request,
            selectedSubtitle,
            [
                new SubtitleItem
                {
                    Position = 1,
                    Lines = ["Hello"]
                }
            ],
            settings);

        Assert.NotNull(reason);
        Assert.Contains("only 1 entries", reason);
    }

    [Fact]
    public void GetUnsafeSourceCancellationReason_ReturnsReason_ForFragmentedAssSource()
    {
        var request = new TranslationRequest
        {
            SourceLanguage = "en",
            TargetLanguage = "pl",
            Title = "Fragmented ASS source",
            MediaType = MediaType.Episode,
            Status = TranslationStatus.Pending,
            SourceSubtitleType = SubtitleLanguageHelper.TypeFull,
            SourceSubtitleEntryCount = 600,
            IsForcedSubtitle = false,
            SourceSubtitleFormat = ".ass"
        };
        var settings = new Dictionary<string, string>
        {
            [SettingKeys.Translation.TranslateSupplementalSubtitles] = "false"
        };

        var reason = TranslationJob.GetUnsafeSourceCancellationReason(
            request,
            selectedSubtitle: null,
            Enumerable.Range(1, 600)
                .Select(index => new SubtitleItem
                {
                    Position = index,
                    Lines = ["a"],
                    PlaintextLines = ["a"],
                    SsaDialogue = new SsaDialogue(),
                    SsaFormat = new SsaFormat()
                })
                .ToList(),
            settings);

        Assert.NotNull(reason);
        Assert.Contains("pathological", reason, StringComparison.OrdinalIgnoreCase);
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
    public async Task ExecuteAsync_FallbackPreservesCachedEmbeddedAssFile_WhenInitialSourceIsEmpty()
    {
        var sourceSubtitlePath = Path.Combine(_tempDirectory, "movie-4.external.en.srt");
        await File.WriteAllTextAsync(sourceSubtitlePath, string.Empty);

        var extractedAssPath = Path.Combine(_tempDirectory, "movie-4.eng.ass");
        await File.WriteAllTextAsync(
            extractedAssPath,
            BuildExtractedAssContent(streamIndex: 2, entries: 50));

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
            .ReturnsAsync(BuildSubtitleItems(50));

        var extractionServiceMock = new Mock<ISubtitleExtractionService>();
        extractionServiceMock
            .Setup(service => service.TryExtractEmbeddedSubtitleForRequestAsync(
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
        var sourceSubtitleResolverMock = new Mock<ISourceSubtitleResolver>();
        sourceSubtitleResolverMock
            .Setup(service => service.ResolveReadableSourcePathAsync(
                It.IsAny<TranslationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((TranslationRequest value, CancellationToken _) => value.SubtitleToTranslate);

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
            sourceSubtitleResolverMock.Object,
            _embeddedSubtitleCacheService,
            Mock.Of<IUploadWorkspaceService>());

        await Assert.ThrowsAsync<TranslationException>(() => job.ExecuteAsync(request.Id, CancellationToken.None));

        Assert.True(File.Exists(extractedAssPath));
        var updatedRequest = await _dbContext.TranslationRequests.SingleAsync(item => item.Id == request.Id);
        Assert.Equal(extractedAssPath, updatedRequest.SubtitleToTranslate);
        Assert.Equal(".ass", updatedRequest.SourceSubtitleFormat);
        Assert.Equal(".ass", updatedRequest.RequiredOutputFormats);
        Assert.Equal("match-source", updatedRequest.SubtitleOutputMode);
        extractionServiceMock.Verify(service => service.TryExtractEmbeddedSubtitleForRequestAsync(
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
    public async Task ExecuteAsync_FallbackPreservesNewlyCachedEmbeddedAssFile_WhenMetadataIsPersistedDuringExtraction()
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
            .ReturnsAsync(BuildSubtitleItems(50));

        var extractionServiceMock = new Mock<ISubtitleExtractionService>();
        extractionServiceMock
            .Setup(service => service.TryExtractEmbeddedSubtitleForRequestAsync(
                request.MediaId!.Value,
                request.MediaType,
                request.SourceLanguage,
                It.IsAny<List<int>?>(),
                It.IsAny<int?>()))
            .Callback<int, MediaType, string, List<int>?, int?>((_, _, _, _, _) =>
            {
                File.WriteAllText(
                    extractedAssPath,
                    BuildExtractedAssContent(streamIndex: 3, entries: 50));

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
        var sourceSubtitleResolverMock = new Mock<ISourceSubtitleResolver>();
        sourceSubtitleResolverMock
            .Setup(service => service.ResolveReadableSourcePathAsync(
                It.IsAny<TranslationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((TranslationRequest value, CancellationToken _) => value.SubtitleToTranslate);

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
            sourceSubtitleResolverMock.Object,
            _embeddedSubtitleCacheService,
            Mock.Of<IUploadWorkspaceService>());

        await Assert.ThrowsAsync<TranslationException>(() => job.ExecuteAsync(request.Id, CancellationToken.None));

        Assert.True(File.Exists(extractedAssPath));
        var updatedRequest = await _dbContext.TranslationRequests.SingleAsync(item => item.Id == request.Id);
        Assert.Equal(extractedAssPath, updatedRequest.SubtitleToTranslate);
        Assert.Equal(".ass", updatedRequest.SourceSubtitleFormat);
        Assert.Equal(".ass", updatedRequest.RequiredOutputFormats);
        Assert.Equal("match-source", updatedRequest.SubtitleOutputMode);
        extractionServiceMock.Verify(service => service.ClearExtractionMetadataAsync(
            request.MediaId!.Value,
            request.MediaType,
            extractedAssPath), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_DualAssOutput_RefreshesStaleQueuedOutputModeAndStoresAllGeneratedSubtitlePaths()
    {
        var sourceSubtitlePath = Path.Combine(_tempDirectory, "movie-6.en.ass");
        await File.WriteAllTextAsync(
            sourceSubtitlePath,
            BuildAssContent(50));

        var movie = CreateMovie(6);
        movie.Path = _tempDirectory;
        var request = new TranslationRequest
        {
            MediaId = movie.Id,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaType = MediaType.Movie,
            WorkloadItemKey = "library:Movie:6",
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = sourceSubtitlePath,
            SourceSubtitleFormat = ".ass",
            SubtitleOutputMode = "match-source",
            RequiredOutputFormats = ".ass",
            IsActive = true
        };

        _dbContext.Movies.Add(movie);
        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var settingServiceMock = new Mock<ISettingService>();
        settingServiceMock
            .Setup(service => service.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> keys) =>
            {
                var settings = keys.ToDictionary(key => key, _ => string.Empty);
                settings[SettingKeys.Translation.ServiceType] = "mock";
                settings[SettingKeys.Translation.FixOverlappingSubtitles] = "false";
                settings[SettingKeys.Translation.StripSubtitleFormatting] = "false";
                settings[SettingKeys.Translation.AddTranslatorInfo] = "false";
                settings[SettingKeys.SubtitleValidation.ValidateSubtitles] = "false";
                settings[SettingKeys.Translation.AiContextPromptEnabled] = "false";
                settings[SettingKeys.Translation.UseBatchTranslation] = "false";
                settings[SettingKeys.Translation.RemoveLanguageTag] = "true";
                settings[SettingKeys.Translation.UseSubtitleTagging] = "true";
                settings[SettingKeys.Translation.SubtitleTag] = "lingarr";
                settings[SettingKeys.Translation.SubtitleTagShort] = "-ai-";
                settings[SettingKeys.Translation.SubtitleOutputMode] = "both";
                settings[SettingKeys.Translation.MaxBatchSplitAttempts] = "3";
                settings[SettingKeys.Translation.BatchRetryMode] = "deferred";
                settings[SettingKeys.Translation.RepairContextRadius] = "10";
                settings[SettingKeys.Translation.RepairMaxRetries] = "1";
                settings[SettingKeys.Translation.StripAssDrawingCommands] = "false";
                settings[SettingKeys.Translation.CleanSourceAssDrawings] = "false";
                settings[SettingKeys.Translation.BatchContextEnabled] = "false";
                return settings;
            });

        var subtitleService = new SubtitleService(NullLogger<SubtitleService>.Instance);
        var translationServiceMock = new Mock<ITranslationService>();
        translationServiceMock
            .Setup(service => service.TranslateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("Czesc");

        var translationServiceFactoryMock = new Mock<ITranslationServiceFactory>();
        translationServiceFactoryMock
            .Setup(factory => factory.CreateTranslationService("mock"))
            .Returns(translationServiceMock.Object);

        var translationRequestServiceMock = new Mock<ITranslationRequestService>();
        translationRequestServiceMock
            .Setup(service => service.UpdateTranslationRequest(
                It.IsAny<TranslationRequest>(),
                It.IsAny<TranslationStatus>(),
                It.IsAny<string?>()))
            .ReturnsAsync((TranslationRequest value, TranslationStatus _, string? _) => value);
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
            .ReturnsAsync(TranslationState.Complete);

        var statisticsServiceMock = new Mock<IStatisticsService>();
        statisticsServiceMock
            .Setup(service => service.UpdateTranslationStatisticsFromSubtitles(
                It.IsAny<TranslationRequest>(),
                It.IsAny<string>(),
                It.IsAny<List<SubtitleItem>>()))
            .ReturnsAsync(0);

        var sourceSnapshotServiceMock = new Mock<ISourceSubtitleSnapshotService>();
        sourceSnapshotServiceMock
            .Setup(service => service.CreateExternalSnapshot(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new SourceSubtitleSnapshot
            {
                Version = SourceSubtitleSnapshot.CurrentVersion,
                SourceType = SourceSubtitleSnapshot.ExternalType,
                SourceLanguage = "en",
                Identity = "external",
                Fingerprint = "fingerprint-external",
                FileSizeBytes = 1
            });
        var sourceSubtitleResolverMock = new Mock<ISourceSubtitleResolver>();
        sourceSubtitleResolverMock
            .Setup(service => service.ResolveReadableSourcePathAsync(
                It.IsAny<TranslationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((TranslationRequest value, CancellationToken _) => value.SubtitleToTranslate);

        var job = new TranslationJob(
            NullLogger<TranslationJob>.Instance,
            settingServiceMock.Object,
            _dbContext,
            progressServiceMock.Object,
            subtitleService,
            Mock.Of<IScheduleService>(),
            statisticsServiceMock.Object,
            translationServiceFactoryMock.Object,
            translationRequestServiceMock.Object,
            Mock.Of<IBatchFallbackService>(),
            Mock.Of<ISubtitleExtractionService>(),
            cancellationServiceMock.Object,
            mediaStateServiceMock.Object,
            Mock.Of<ICustomMediaStateService>(),
            Mock.Of<IDeferredRepairService>(),
            Mock.Of<IDashboardService>(),
            sourceSnapshotServiceMock.Object,
            sourceSubtitleResolverMock.Object,
            _embeddedSubtitleCacheService,
            Mock.Of<IUploadWorkspaceService>());

        await job.ExecuteAsync(request.Id, CancellationToken.None);

        var updatedRequest = await _dbContext.TranslationRequests.SingleAsync(item => item.Id == request.Id);
        var generatedPaths = JsonSerializer.Deserialize<List<string>>(updatedRequest.GeneratedSubtitlePaths!);

        Assert.Equal(".ass,.srt", updatedRequest.GeneratedOutputFormats);
        Assert.Equal(".ass,.srt", updatedRequest.RequiredOutputFormats);
        Assert.Equal("both", updatedRequest.SubtitleOutputMode);
        Assert.NotNull(generatedPaths);
        Assert.Equal(2, generatedPaths.Count);
        Assert.Contains(generatedPaths, path => Path.GetExtension(path) == ".ass" && File.Exists(path));
        Assert.Contains(generatedPaths, path => Path.GetExtension(path) == ".srt" && File.Exists(path));
    }

    [Fact]
    public async Task ExecuteAsync_WhenTranslatedOutputEchoesSource_QuarantinesOutputAndFailsRequest()
    {
        var sourceSubtitlePath = Path.Combine(_tempDirectory, "cars.en.srt");
        await File.WriteAllTextAsync(
            sourceSubtitlePath,
            string.Join(
                Environment.NewLine + Environment.NewLine,
                Enumerable.Range(1, SubtitleExtractionService.MinimumDialogueEntries).Select(index =>
                {
                    var start = TimeSpan.FromSeconds(index);
                    var end = TimeSpan.FromSeconds(index + 1);
                    return $"{index}{Environment.NewLine}{start:hh\\:mm\\:ss},000 --> {end:hh\\:mm\\:ss},000{Environment.NewLine}English source line number {index}";
                })));

        var movie = CreateMovie(7);
        movie.Path = _tempDirectory;
        var request = new TranslationRequest
        {
            MediaId = movie.Id,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaType = MediaType.Movie,
            WorkloadItemKey = "library:Movie:7",
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = sourceSubtitlePath,
            SourceSubtitleFormat = ".srt",
            SubtitleOutputMode = "match-source",
            RequiredOutputFormats = ".srt",
            IsActive = true
        };

        _dbContext.Movies.Add(movie);
        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var quarantineRoot = Path.Combine(_tempDirectory, "quarantine");
        var previousQuarantineRoot = Environment.GetEnvironmentVariable("LINGARR_TRANSLATION_QUARANTINE_PATH");
        Environment.SetEnvironmentVariable("LINGARR_TRANSLATION_QUARANTINE_PATH", quarantineRoot);

        try
        {
            var settingServiceMock = new Mock<ISettingService>();
            settingServiceMock
                .Setup(service => service.GetSettings(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync((IEnumerable<string> keys) =>
                {
                    var settings = keys.ToDictionary(key => key, _ => string.Empty);
                    settings[SettingKeys.Translation.ServiceType] = "mock";
                    settings[SettingKeys.Translation.FixOverlappingSubtitles] = "false";
                    settings[SettingKeys.Translation.StripSubtitleFormatting] = "false";
                    settings[SettingKeys.Translation.AddTranslatorInfo] = "false";
                    settings[SettingKeys.SubtitleValidation.ValidateSubtitles] = "false";
                    settings[SettingKeys.Translation.AiContextPromptEnabled] = "false";
                    settings[SettingKeys.Translation.UseBatchTranslation] = "false";
                    settings[SettingKeys.Translation.RemoveLanguageTag] = "false";
                    settings[SettingKeys.Translation.UseSubtitleTagging] = "true";
                    settings[SettingKeys.Translation.SubtitleTag] = "lingarr";
                    settings[SettingKeys.Translation.SubtitleTagShort] = "-ai-";
                    settings[SettingKeys.Translation.SubtitleOutputMode] = "match-source";
                    settings[SettingKeys.Translation.MaxBatchSplitAttempts] = "3";
                    settings[SettingKeys.Translation.BatchRetryMode] = "deferred";
                    settings[SettingKeys.Translation.RepairContextRadius] = "10";
                    settings[SettingKeys.Translation.RepairMaxRetries] = "1";
                    settings[SettingKeys.Translation.StripAssDrawingCommands] = "false";
                    settings[SettingKeys.Translation.CleanSourceAssDrawings] = "false";
                    settings[SettingKeys.Translation.BatchContextEnabled] = "false";
                    settings[SettingKeys.Translation.EnablePostTranslationQualityGate] = "true";
                    return settings;
                });
            settingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.Translation.EnablePostTranslationQualityGate))
                .ReturnsAsync("true");

            var subtitleService = new SubtitleService(NullLogger<SubtitleService>.Instance);
            var translationServiceMock = new Mock<ITranslationService>();
            translationServiceMock
                .Setup(service => service.TranslateAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<List<string>?>(),
                    It.IsAny<List<string>?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((string text, string _, string _, List<string>? _, List<string>? _, CancellationToken _) => text);

            var translationServiceFactoryMock = new Mock<ITranslationServiceFactory>();
            translationServiceFactoryMock
                .Setup(factory => factory.CreateTranslationService("mock"))
                .Returns(translationServiceMock.Object);

            var translationRequestServiceMock = new Mock<ITranslationRequestService>();
            translationRequestServiceMock
                .Setup(service => service.UpdateTranslationRequest(
                    It.IsAny<TranslationRequest>(),
                    It.IsAny<TranslationStatus>(),
                    It.IsAny<string?>()))
                .ReturnsAsync((TranslationRequest value, TranslationStatus status, string? _) =>
                {
                    value.Status = status;
                    value.IsActive = null;
                    return value;
                });
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
                .ReturnsAsync(TranslationState.Failed);

            var statisticsServiceMock = new Mock<IStatisticsService>();
            statisticsServiceMock
                .Setup(service => service.UpdateTranslationStatisticsFromSubtitles(
                    It.IsAny<TranslationRequest>(),
                    It.IsAny<string>(),
                    It.IsAny<List<SubtitleItem>>()))
                .ReturnsAsync(0);

            var sourceSnapshotServiceMock = new Mock<ISourceSubtitleSnapshotService>();
            sourceSnapshotServiceMock
                .Setup(service => service.CreateExternalSnapshot(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new SourceSubtitleSnapshot
                {
                    Version = SourceSubtitleSnapshot.CurrentVersion,
                    SourceType = SourceSubtitleSnapshot.ExternalType,
                    SourceLanguage = "en",
                    Identity = "external",
                    Fingerprint = "fingerprint-external",
                    FileSizeBytes = 1
                });
            var sourceSubtitleResolverMock = new Mock<ISourceSubtitleResolver>();
            sourceSubtitleResolverMock
                .Setup(service => service.ResolveReadableSourcePathAsync(
                    It.IsAny<TranslationRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((TranslationRequest value, CancellationToken _) => value.SubtitleToTranslate);

            var diagnosticsService = new TranslationDiagnosticsService(
                _dbContext,
                NullLogger<TranslationDiagnosticsService>.Instance);
            var qualityServiceMock = new Mock<ISubtitleQualityValidatorService>();
            qualityServiceMock
                .Setup(service => service.ValidateAsync(
                    It.IsAny<SubtitleQualityValidationRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SubtitleQualityValidationResult
                {
                    IsValid = false,
                    Summary = "Found mostly unchanged source text in translated output.",
                    SourceEntryCount = SubtitleExtractionService.MinimumDialogueEntries,
                    TargetEntryCount = SubtitleExtractionService.MinimumDialogueEntries,
                    MinimumTargetEntryCount = SubtitleExtractionService.MinimumDialogueEntries - 10,
                    IssueTypes = [SubtitleQualityIssueCodes.UnchangedSourceText],
                    SampleLines = ["English source line 1"]
                });
            var job = new TranslationJob(
                NullLogger<TranslationJob>.Instance,
                settingServiceMock.Object,
                _dbContext,
                progressServiceMock.Object,
                subtitleService,
                Mock.Of<IScheduleService>(),
                statisticsServiceMock.Object,
                translationServiceFactoryMock.Object,
                translationRequestServiceMock.Object,
                Mock.Of<IBatchFallbackService>(),
                Mock.Of<ISubtitleExtractionService>(),
                cancellationServiceMock.Object,
                mediaStateServiceMock.Object,
                Mock.Of<ICustomMediaStateService>(),
                Mock.Of<IDeferredRepairService>(),
                Mock.Of<IDashboardService>(),
                sourceSnapshotServiceMock.Object,
                sourceSubtitleResolverMock.Object,
                _embeddedSubtitleCacheService,
                Mock.Of<IUploadWorkspaceService>(),
                null,
                null,
                qualityServiceMock.Object,
                diagnosticsService);

            await Assert.ThrowsAsync<TranslationException>(() => job.ExecuteAsync(request.Id, CancellationToken.None));

            var finalPath = Path.Combine(_tempDirectory, "cars.pl.lingarr.srt");
            var updatedRequest = await _dbContext.TranslationRequests.SingleAsync(item => item.Id == request.Id);
            var diagnostic = await _dbContext.TranslationDiagnosticEvents.SingleAsync();

            Assert.Equal(TranslationStatus.Failed, updatedRequest.Status);
            Assert.False(File.Exists(finalPath));
            Assert.NotNull(diagnostic.QuarantinePath);
            Assert.True(File.Exists(diagnostic.QuarantinePath));
            Assert.Equal(SubtitleQualityIssueCodes.UnchangedSourceText, diagnostic.ReasonCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LINGARR_TRANSLATION_QUARANTINE_PATH", previousQuarantineRoot);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenSelectedAssSourceHasNoDialogues_FallsBackToDifferentEmbeddedTextStream()
    {
        var sourceSubtitlePath = Path.Combine(_tempDirectory, "empty-source.eng.ass");
        var fallbackSubtitlePath = Path.Combine(_tempDirectory, "fallback-source.eng.srt");
        await File.WriteAllTextAsync(sourceSubtitlePath, "[Events]");
        await File.WriteAllTextAsync(fallbackSubtitlePath, "1\n00:00:01,000 --> 00:00:02,000\nHello\n");

        var movie = CreateMovie(8);
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 2,
            Language = "eng",
            CodecName = "ass",
            IsTextBased = true,
            IsExtracted = true,
            ExtractedPath = sourceSubtitlePath
        });
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 3,
            Language = "eng",
            CodecName = "subrip",
            IsTextBased = true,
            IsExtracted = true,
            ExtractedPath = fallbackSubtitlePath
        });

        var request = new TranslationRequest
        {
            MediaId = movie.Id,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = sourceSubtitlePath,
            SourceSnapshotStreamIndex = 2
        };

        _dbContext.Movies.Add(movie);
        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var subtitleServiceMock = new Mock<ISubtitleService>();
        subtitleServiceMock
            .Setup(service => service.ReadSubtitles(sourceSubtitlePath))
            .ThrowsAsync(new ArgumentException("No valid subtitles found in SSA format"));
        subtitleServiceMock
            .Setup(service => service.ReadSubtitles(fallbackSubtitlePath))
            .ReturnsAsync(BuildSubtitleItems(50));

        List<int>? excludedStreams = null;
        var extractionServiceMock = new Mock<ISubtitleExtractionService>();
        extractionServiceMock
            .Setup(service => service.TryExtractEmbeddedSubtitleForRequestAsync(
                movie.Id,
                MediaType.Movie,
                "en",
                It.IsAny<List<int>?>(),
                It.IsAny<int?>()))
            .Callback((int _, MediaType _, string _, List<int>? excluded, int? _) =>
            {
                excludedStreams = excluded?.ToList();
            })
            .ReturnsAsync(fallbackSubtitlePath);

        var translationServiceMock = new Mock<ITranslationService>();
        translationServiceMock
            .Setup(service => service.TranslateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TranslationException("forced-test-failure-after-fallback"));

        var job = BuildExecutableJob(
            subtitleServiceMock.Object,
            extractionServiceMock.Object,
            translationServiceMock.Object);

        await Assert.ThrowsAsync<TranslationException>(() => job.ExecuteAsync(request.Id, CancellationToken.None));

        Assert.NotNull(excludedStreams);
        Assert.Contains(2, excludedStreams!);
        Assert.Equal(fallbackSubtitlePath, request.SubtitleToTranslate);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFirstFinalPublishPathFails_TriesNextFallbackPath()
    {
        var sourceSubtitlePath = Path.Combine(_tempDirectory, "source.en.srt");
        await File.WriteAllTextAsync(sourceSubtitlePath, "1\n00:00:01,000 --> 00:00:02,000\nHello\n");

        var movie = CreateMovie(9);
        movie.Path = _tempDirectory;
        var request = new TranslationRequest
        {
            MediaId = movie.Id,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = sourceSubtitlePath,
            SourceSubtitleFormat = ".srt",
            SubtitleOutputMode = "match-source",
            RequiredOutputFormats = ".srt",
            IsActive = true
        };

        _dbContext.Movies.Add(movie);
        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var badFinalPath = Path.Combine(_tempDirectory, new string('a', 300), "source.pl.lingarr.srt");
        var goodFinalPath = Path.Combine(_tempDirectory, "source.pl.ai.srt");

        var subtitleServiceMock = new Mock<ISubtitleService>();
        subtitleServiceMock
            .Setup(service => service.ReadSubtitles(sourceSubtitlePath))
            .ReturnsAsync(BuildSubtitleItems(50));
        subtitleServiceMock
            .Setup(service => service.WriteSubtitles(
                It.IsAny<string>(),
                It.IsAny<List<SubtitleItem>>(),
                It.IsAny<bool>()))
            .Returns((string path, List<SubtitleItem> subtitles, bool _) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                return File.WriteAllTextAsync(path, string.Join(Environment.NewLine, subtitles.SelectMany(item => item.TranslatedLines)));
            });
        subtitleServiceMock
            .Setup(service => service.CreateFallbackPaths(
                sourceSubtitlePath,
                "pl",
                "lingarr",
                "-ai-",
                ".srt",
                null))
            .Returns([badFinalPath, goodFinalPath]);

        var translationServiceMock = new Mock<ITranslationService>();
        translationServiceMock
            .Setup(service => service.TranslateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("Czesc");

        var qualityServiceMock = new Mock<ISubtitleQualityValidatorService>();
        qualityServiceMock
            .Setup(service => service.ValidateAsync(
                It.IsAny<SubtitleQualityValidationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubtitleQualityValidationResult
            {
                IsValid = true,
                Summary = "ok",
                SourceEntryCount = 50,
                TargetEntryCount = 50,
                MinimumTargetEntryCount = 45
            });

        var job = BuildExecutableJob(
            subtitleServiceMock.Object,
            Mock.Of<ISubtitleExtractionService>(),
            translationServiceMock.Object,
            qualityServiceMock.Object);

        await job.ExecuteAsync(request.Id, CancellationToken.None);

        var updatedRequest = await _dbContext.TranslationRequests.SingleAsync(item => item.Id == request.Id);
        Assert.Equal(TranslationStatus.Completed, updatedRequest.Status);
        Assert.Equal(goodFinalPath, updatedRequest.TranslatedSubtitle);
        Assert.True(File.Exists(goodFinalPath));
    }

    [Fact]
    public async Task ExecuteAsync_WithForcedDialogueSource_DoesNotRequestForcedOutputCaption()
    {
        var sourceSubtitlePath = Path.Combine(_tempDirectory, "source.en.srt");
        await File.WriteAllTextAsync(sourceSubtitlePath, "1\n00:00:01,000 --> 00:00:02,000\nHello\n");

        var movie = CreateMovie(90);
        movie.Path = _tempDirectory;
        var request = new TranslationRequest
        {
            MediaId = movie.Id,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = sourceSubtitlePath,
            SourceSubtitleFormat = ".srt",
            SubtitleOutputMode = "match-source",
            RequiredOutputFormats = ".srt",
            SourceSubtitleType = SubtitleLanguageHelper.TypeForcedDialogue,
            IsForcedSubtitle = true,
            IsActive = true
        };

        _dbContext.Movies.Add(movie);
        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var finalPath = Path.Combine(_tempDirectory, "source.pl.-ai-.srt");
        var forcedPath = Path.Combine(_tempDirectory, "source.pl.forced.-ai-.srt");

        var subtitleServiceMock = new Mock<ISubtitleService>();
        subtitleServiceMock
            .Setup(service => service.ReadSubtitles(sourceSubtitlePath))
            .ReturnsAsync(BuildSubtitleItems(50));
        subtitleServiceMock
            .Setup(service => service.WriteSubtitles(
                It.IsAny<string>(),
                It.IsAny<List<SubtitleItem>>(),
                It.IsAny<bool>()))
            .Returns((string path, List<SubtitleItem> subtitles, bool _) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                return File.WriteAllTextAsync(path, string.Join(Environment.NewLine, subtitles.SelectMany(item => item.TranslatedLines)));
            });
        subtitleServiceMock
            .Setup(service => service.CreateFallbackPaths(
                sourceSubtitlePath,
                "pl",
                "lingarr",
                "-ai-",
                ".srt",
                null))
            .Returns([finalPath]);
        subtitleServiceMock
            .Setup(service => service.CreateFallbackPaths(
                sourceSubtitlePath,
                "pl",
                "lingarr",
                "-ai-",
                ".srt",
                "forced"))
            .Returns([forcedPath]);

        var translationServiceMock = new Mock<ITranslationService>();
        translationServiceMock
            .Setup(service => service.TranslateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("Czesc");

        var qualityServiceMock = new Mock<ISubtitleQualityValidatorService>();
        qualityServiceMock
            .Setup(service => service.ValidateAsync(
                It.IsAny<SubtitleQualityValidationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubtitleQualityValidationResult
            {
                IsValid = true,
                Summary = "ok",
                SourceEntryCount = 50,
                TargetEntryCount = 50,
                MinimumTargetEntryCount = 45
            });

        var job = BuildExecutableJob(
            subtitleServiceMock.Object,
            Mock.Of<ISubtitleExtractionService>(),
            translationServiceMock.Object,
            qualityServiceMock.Object);

        await job.ExecuteAsync(request.Id, CancellationToken.None);

        var updatedRequest = await _dbContext.TranslationRequests.SingleAsync(item => item.Id == request.Id);
        Assert.Equal(TranslationStatus.Completed, updatedRequest.Status);
        Assert.Equal(finalPath, updatedRequest.TranslatedSubtitle);
        subtitleServiceMock.Verify(
            service => service.CreateFallbackPaths(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                "forced"),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPublishingTaggedOutput_RemovesStaleTaggedFallbackSiblings()
    {
        var sourceSubtitlePath = Path.Combine(_tempDirectory, "source.en.srt");
        await File.WriteAllTextAsync(sourceSubtitlePath, "source");

        var movie = CreateMovie(10);
        movie.Path = _tempDirectory;
        var request = new TranslationRequest
        {
            MediaId = movie.Id,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = sourceSubtitlePath,
            SourceSubtitleFormat = ".srt",
            SubtitleOutputMode = "match-source",
            RequiredOutputFormats = ".srt",
            IsActive = true
        };

        _dbContext.Movies.Add(movie);
        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var currentTaggedPath = Path.Combine(_tempDirectory, "source.pl.-ai-.srt");
        var staleTaggedPath = Path.Combine(_tempDirectory, "source.pl.lingarr.srt");
        var untaggedPath = Path.Combine(_tempDirectory, "source.pl.srt");
        await File.WriteAllTextAsync(staleTaggedPath, "stale tagged output");
        await File.WriteAllTextAsync(untaggedPath, "manual subtitle");

        var subtitleServiceMock = new Mock<ISubtitleService>();
        subtitleServiceMock
            .Setup(service => service.ReadSubtitles(sourceSubtitlePath))
            .ReturnsAsync(BuildSubtitleItems(50));
        subtitleServiceMock
            .Setup(service => service.WriteSubtitles(
                It.IsAny<string>(),
                It.IsAny<List<SubtitleItem>>(),
                It.IsAny<bool>()))
            .Returns((string path, List<SubtitleItem> subtitles, bool _) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                return File.WriteAllTextAsync(
                    path,
                    string.Join(Environment.NewLine, subtitles.SelectMany(item => item.TranslatedLines)));
            });
        subtitleServiceMock
            .Setup(service => service.CreateFallbackPaths(
                sourceSubtitlePath,
                "pl",
                "lingarr",
                "-ai-",
                ".srt",
                null))
            .Returns([currentTaggedPath, staleTaggedPath, untaggedPath]);

        var translationServiceMock = new Mock<ITranslationService>();
        translationServiceMock
            .Setup(service => service.TranslateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("Czesc");

        var qualityServiceMock = new Mock<ISubtitleQualityValidatorService>();
        qualityServiceMock
            .Setup(service => service.ValidateAsync(
                It.IsAny<SubtitleQualityValidationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubtitleQualityValidationResult
            {
                IsValid = true,
                Summary = "ok",
                SourceEntryCount = 50,
                TargetEntryCount = 50,
                MinimumTargetEntryCount = 45
            });

        var job = BuildExecutableJob(
            subtitleServiceMock.Object,
            Mock.Of<ISubtitleExtractionService>(),
            translationServiceMock.Object,
            qualityServiceMock.Object);

        await job.ExecuteAsync(request.Id, CancellationToken.None);

        var updatedRequest = await _dbContext.TranslationRequests.SingleAsync(item => item.Id == request.Id);
        Assert.Equal(TranslationStatus.Completed, updatedRequest.Status);
        Assert.Equal(currentTaggedPath, updatedRequest.TranslatedSubtitle);
        Assert.True(File.Exists(currentTaggedPath));
        Assert.False(File.Exists(staleTaggedPath));
        Assert.True(File.Exists(untaggedPath));
    }

    [Fact]
    public async Task ExecuteAsync_WhenEmbeddedSourceMediaFileNameHasNoExtension_PreservesVideoBaseName()
    {
        var mediaBaseName = "Movie 11 [Bluray-1080p][DTS-HD MA 5.1]-FraMeSToR";
        var mediaPath = Path.Combine(_tempDirectory, mediaBaseName + ".mkv");
        await File.WriteAllTextAsync(mediaPath, string.Empty);

        var movie = CreateMovie(11);
        movie.Path = _tempDirectory;
        movie.FileName = mediaBaseName;
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var sourceSubtitlePath = _embeddedSubtitleCacheService.GetOcrCachePath(
            movie.Id,
            MediaType.Movie,
            streamIndex: 1,
            language: "en");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceSubtitlePath)!);
        await File.WriteAllTextAsync(
            sourceSubtitlePath,
            string.Join(
                Environment.NewLine + Environment.NewLine,
                Enumerable.Range(1, 50).Select(index =>
                {
                    var start = TimeSpan.FromSeconds(index);
                    var end = TimeSpan.FromSeconds(index + 1);
                    return $"{index}{Environment.NewLine}{start:hh\\:mm\\:ss},000 --> {end:hh\\:mm\\:ss},000{Environment.NewLine}Hello {index}";
                })));

        var request = new TranslationRequest
        {
            MediaId = movie.Id,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = sourceSubtitlePath,
            SourceSubtitleFormat = ".srt",
            SubtitleOutputMode = "match-source",
            RequiredOutputFormats = ".srt",
            WorkloadKind = TranslationWorkloadKind.Library,
            IsActive = true
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var translationServiceMock = new Mock<ITranslationService>();
        translationServiceMock
            .Setup(service => service.TranslateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("Czesc");

        var qualityServiceMock = new Mock<ISubtitleQualityValidatorService>();
        qualityServiceMock
            .Setup(service => service.ValidateAsync(
                It.IsAny<SubtitleQualityValidationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubtitleQualityValidationResult
            {
                IsValid = true,
                Summary = "ok",
                SourceEntryCount = 50,
                TargetEntryCount = 50,
                MinimumTargetEntryCount = 45
            });

        var job = BuildExecutableJob(
            new SubtitleService(NullLogger<SubtitleService>.Instance),
            Mock.Of<ISubtitleExtractionService>(),
            translationServiceMock.Object,
            qualityServiceMock.Object);

        await job.ExecuteAsync(request.Id, CancellationToken.None);

        var expectedPath = Path.Combine(_tempDirectory, mediaBaseName + ".pl.lingarr.srt");
        var choppedPath = Path.Combine(_tempDirectory, "Movie 11 [Bluray-1080p][DTS-HD MA 5.pl.lingarr.srt");
        var updatedRequest = await _dbContext.TranslationRequests.SingleAsync(item => item.Id == request.Id);
        Assert.Equal(expectedPath, updatedRequest.TranslatedSubtitle);
        Assert.True(File.Exists(expectedPath));
        Assert.False(File.Exists(choppedPath));
    }

    [Fact]
    public async Task ExecuteAsync_WhenOnlyOutputCandidateIsSourcePath_FailsInsteadOfPublishingSourceAsTranslation()
    {
        var sourceSubtitlePath = Path.Combine(_tempDirectory, "source.jpn.ass");
        await File.WriteAllTextAsync(sourceSubtitlePath, BuildAssContent(50));

        var movie = CreateMovie(12);
        movie.Path = _tempDirectory;
        _dbContext.Movies.Add(movie);

        var request = new TranslationRequest
        {
            MediaId = movie.Id,
            Title = movie.Title,
            SourceLanguage = "ja",
            TargetLanguage = "pl",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = sourceSubtitlePath,
            SourceSubtitleFormat = ".ass",
            SubtitleOutputMode = "match-source",
            RequiredOutputFormats = ".ass",
            IsActive = true
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var subtitleServiceMock = new Mock<ISubtitleService>();
        subtitleServiceMock
            .Setup(service => service.ReadSubtitles(sourceSubtitlePath))
            .ReturnsAsync(BuildSubtitleItems(50));
        subtitleServiceMock
            .Setup(service => service.WriteSubtitles(
                It.IsAny<string>(),
                It.IsAny<List<SubtitleItem>>(),
                It.IsAny<bool>()))
            .Returns((string path, List<SubtitleItem> subtitles, bool _) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                return File.WriteAllTextAsync(
                    path,
                    string.Join(Environment.NewLine, subtitles.SelectMany(item => item.TranslatedLines)));
            });
        subtitleServiceMock
            .Setup(service => service.CreateFallbackPaths(
                sourceSubtitlePath,
                "pl",
                "lingarr",
                "-ai-",
                ".ass",
                null))
            .Returns([sourceSubtitlePath]);

        var translationServiceMock = new Mock<ITranslationService>();
        translationServiceMock
            .Setup(service => service.TranslateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("Czesc");

        var qualityServiceMock = new Mock<ISubtitleQualityValidatorService>();
        qualityServiceMock
            .Setup(service => service.ValidateAsync(
                It.IsAny<SubtitleQualityValidationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubtitleQualityValidationResult
            {
                IsValid = true,
                Summary = "ok",
                SourceEntryCount = 50,
                TargetEntryCount = 50,
                MinimumTargetEntryCount = 45
            });

        var job = BuildExecutableJob(
            subtitleServiceMock.Object,
            Mock.Of<ISubtitleExtractionService>(),
            translationServiceMock.Object,
            qualityServiceMock.Object);

        await Assert.ThrowsAsync<Exception>(() => job.ExecuteAsync(request.Id, CancellationToken.None));

        var updatedRequest = await _dbContext.TranslationRequests.SingleAsync(item => item.Id == request.Id);
        Assert.Equal(TranslationStatus.Failed, updatedRequest.Status);
        Assert.NotEqual(sourceSubtitlePath, updatedRequest.TranslatedSubtitle);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEmbeddingInMkv_RefreshesEmbeddedSubtitleIndexBeforeStateRefresh()
    {
        var mediaPath = Path.Combine(_tempDirectory, "movie-20.mkv");
        await File.WriteAllTextAsync(mediaPath, "fake mkv");

        var movie = CreateMovie(20);
        movie.Path = _tempDirectory;
        movie.FileName = Path.GetFileName(mediaPath);

        var sourceSubtitlePath = _embeddedSubtitleCacheService.GetCachePath(
            movie.Id,
            MediaType.Movie,
            streamIndex: 0,
            codecName: "subrip",
            language: "eng");
        await File.WriteAllTextAsync(sourceSubtitlePath, BuildSrtContent(50));

        var request = new TranslationRequest
        {
            MediaId = movie.Id,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaType = MediaType.Movie,
            WorkloadKind = TranslationWorkloadKind.Library,
            WorkloadItemKey = "library:Movie:20",
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = sourceSubtitlePath,
            SourceSubtitleFormat = ".srt",
            SubtitleOutputMode = "match-source",
            RequiredOutputFormats = ".srt",
            IsActive = true
        };

        _dbContext.Movies.Add(movie);
        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var subtitleService = new SubtitleService(NullLogger<SubtitleService>.Instance);
        var extractionServiceMock = new Mock<ISubtitleExtractionService>();
        extractionServiceMock
            .Setup(service => service.SyncEmbeddedSubtitles(It.IsAny<Movie>()))
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
            .ReturnsAsync("Czesc");

        var mkvEmbeddingServiceMock = new Mock<IMkvEmbeddingService>();
        mkvEmbeddingServiceMock
            .Setup(service => service.WouldExceedPathLimit(It.IsAny<string>()))
            .Returns(false);
        mkvEmbeddingServiceMock
            .Setup(service => service.EmbedSubtitleAsync(
                mediaPath,
                It.IsAny<string>(),
                "pl",
                "pl (Lingarr)",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MkvEmbedResult(true, mediaPath));

        var qualityServiceMock = CreatePassingQualityValidator();
        var job = BuildExecutableJob(
            subtitleService,
            extractionServiceMock.Object,
            translationServiceMock.Object,
            qualityServiceMock.Object,
            new Dictionary<string, string>
            {
                [SettingKeys.Translation.EmbedInContainer] = "true"
            },
            mkvEmbeddingServiceMock.Object);

        await job.ExecuteAsync(request.Id, CancellationToken.None);

        extractionServiceMock.Verify(
            service => service.SyncEmbeddedSubtitles(It.Is<Movie>(item => item.Id == movie.Id)),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenWritingSidecar_DoesNotRefreshEmbeddedSubtitleIndex()
    {
        var sourceSubtitlePath = Path.Combine(_tempDirectory, "movie-21.en.srt");
        await File.WriteAllTextAsync(sourceSubtitlePath, BuildSrtContent(50));

        var movie = CreateMovie(21);
        movie.Path = _tempDirectory;
        var request = new TranslationRequest
        {
            MediaId = movie.Id,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaType = MediaType.Movie,
            WorkloadKind = TranslationWorkloadKind.Library,
            WorkloadItemKey = "library:Movie:21",
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = sourceSubtitlePath,
            SourceSubtitleFormat = ".srt",
            SubtitleOutputMode = "match-source",
            RequiredOutputFormats = ".srt",
            IsActive = true
        };

        _dbContext.Movies.Add(movie);
        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var subtitleService = new SubtitleService(NullLogger<SubtitleService>.Instance);
        var extractionServiceMock = new Mock<ISubtitleExtractionService>();
        var translationServiceMock = new Mock<ITranslationService>();
        translationServiceMock
            .Setup(service => service.TranslateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("Czesc");

        var qualityServiceMock = CreatePassingQualityValidator();
        var job = BuildExecutableJob(
            subtitleService,
            extractionServiceMock.Object,
            translationServiceMock.Object,
            qualityServiceMock.Object);

        await job.ExecuteAsync(request.Id, CancellationToken.None);

        extractionServiceMock.Verify(
            service => service.SyncEmbeddedSubtitles(It.IsAny<Movie>()),
            Times.Never);
        extractionServiceMock.Verify(
            service => service.SyncEmbeddedSubtitles(It.IsAny<Episode>()),
            Times.Never);
    }

    private static List<SubtitleItem> BuildSubtitleItems(int count)
    {
        return Enumerable.Range(1, count)
            .Select(index => new SubtitleItem
            {
                Position = index,
                StartTime = index * 1_000,
                EndTime = index * 1_000 + 500,
                Lines = [$"Hello {index}"],
                PlaintextLines = [$"Hello {index}"],
                TranslatedLines = []
            })
            .ToList();
    }

    private TranslationJob BuildExecutableJob(
        ISubtitleService subtitleService,
        ISubtitleExtractionService extractionService,
        ITranslationService translationService,
        ISubtitleQualityValidatorService? qualityValidatorService = null,
        IReadOnlyDictionary<string, string>? settingOverrides = null,
        IMkvEmbeddingService? mkvEmbeddingService = null)
    {
        var settingServiceMock = new Mock<ISettingService>();
        settingServiceMock
            .Setup(service => service.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> keys) =>
            {
                var settings = keys.ToDictionary(key => key, _ => string.Empty);
                settings[SettingKeys.Translation.ServiceType] = "mock";
                settings[SettingKeys.Translation.FixOverlappingSubtitles] = "false";
                settings[SettingKeys.Translation.StripSubtitleFormatting] = "false";
                settings[SettingKeys.Translation.AddTranslatorInfo] = "false";
                settings[SettingKeys.SubtitleValidation.ValidateSubtitles] = "false";
                settings[SettingKeys.Translation.AiContextPromptEnabled] = "false";
                settings[SettingKeys.Translation.UseBatchTranslation] = "false";
                settings[SettingKeys.Translation.RemoveLanguageTag] = "false";
                settings[SettingKeys.Translation.UseSubtitleTagging] = "true";
                settings[SettingKeys.Translation.SubtitleTag] = "lingarr";
                settings[SettingKeys.Translation.SubtitleTagShort] = "-ai-";
                settings[SettingKeys.Translation.SubtitleOutputMode] = "match-source";
                settings[SettingKeys.Translation.MaxBatchSplitAttempts] = "3";
                settings[SettingKeys.Translation.BatchRetryMode] = "deferred";
                settings[SettingKeys.Translation.RepairContextRadius] = "10";
                settings[SettingKeys.Translation.RepairMaxRetries] = "1";
                settings[SettingKeys.Translation.StripAssDrawingCommands] = "false";
                settings[SettingKeys.Translation.CleanSourceAssDrawings] = "false";
                settings[SettingKeys.Translation.BatchContextEnabled] = "false";
                if (settingOverrides != null)
                {
                    foreach (var (key, value) in settingOverrides)
                    {
                        settings[key] = value;
                    }
                }

                return settings;
            });

        var translationServiceFactoryMock = new Mock<ITranslationServiceFactory>();
        translationServiceFactoryMock
            .Setup(factory => factory.CreateTranslationService("mock"))
            .Returns(translationService);

        var translationRequestServiceMock = new Mock<ITranslationRequestService>();
        translationRequestServiceMock
            .Setup(service => service.UpdateTranslationRequest(
                It.IsAny<TranslationRequest>(),
                It.IsAny<TranslationStatus>(),
                It.IsAny<string?>()))
            .ReturnsAsync((TranslationRequest value, TranslationStatus status, string? _) =>
            {
                value.Status = status;
                value.IsActive = null;
                return value;
            });
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
            .ReturnsAsync(TranslationState.Complete);

        var statisticsServiceMock = new Mock<IStatisticsService>();
        statisticsServiceMock
            .Setup(service => service.UpdateTranslationStatisticsFromSubtitles(
                It.IsAny<TranslationRequest>(),
                It.IsAny<string>(),
                It.IsAny<List<SubtitleItem>>()))
            .ReturnsAsync(0);

        var dashboardServiceMock = new Mock<IDashboardService>();
        dashboardServiceMock
            .Setup(service => service.LogError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var sourceSnapshotServiceMock = new Mock<ISourceSubtitleSnapshotService>();
        sourceSnapshotServiceMock
            .Setup(service => service.CreateExternalSnapshot(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new SourceSubtitleSnapshot
            {
                Version = SourceSubtitleSnapshot.CurrentVersion,
                SourceType = SourceSubtitleSnapshot.ExternalType,
                SourceLanguage = "en",
                Identity = "external",
                Fingerprint = "fingerprint-external",
                FileSizeBytes = 1
            });
        sourceSnapshotServiceMock
            .Setup(service => service.CreateEmbeddedSnapshot(It.IsAny<EmbeddedSubtitle>(), It.IsAny<string>()))
            .Returns(new SourceSubtitleSnapshot
            {
                Version = SourceSubtitleSnapshot.CurrentVersion,
                SourceType = SourceSubtitleSnapshot.EmbeddedType,
                SourceLanguage = "en",
                Identity = "embedded",
                Fingerprint = "fingerprint-embedded",
                FileSizeBytes = 1,
                StreamIndex = 3
            });

        var sourceSubtitleResolverMock = new Mock<ISourceSubtitleResolver>();
        sourceSubtitleResolverMock
            .Setup(service => service.ResolveReadableSourcePathAsync(
                It.IsAny<TranslationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((TranslationRequest value, CancellationToken _) => value.SubtitleToTranslate);

        return new TranslationJob(
            NullLogger<TranslationJob>.Instance,
            settingServiceMock.Object,
            _dbContext,
            progressServiceMock.Object,
            subtitleService,
            Mock.Of<IScheduleService>(),
            statisticsServiceMock.Object,
            translationServiceFactoryMock.Object,
            translationRequestServiceMock.Object,
            Mock.Of<IBatchFallbackService>(),
            extractionService,
            cancellationServiceMock.Object,
            mediaStateServiceMock.Object,
            Mock.Of<ICustomMediaStateService>(),
            Mock.Of<IDeferredRepairService>(),
            dashboardServiceMock.Object,
            sourceSnapshotServiceMock.Object,
            sourceSubtitleResolverMock.Object,
            _embeddedSubtitleCacheService,
            Mock.Of<IUploadWorkspaceService>(),
            null,
            null,
            qualityValidatorService,
            null,
            null,
            mkvEmbeddingService);
    }

    private static Mock<ISubtitleQualityValidatorService> CreatePassingQualityValidator()
    {
        var qualityServiceMock = new Mock<ISubtitleQualityValidatorService>();
        qualityServiceMock
            .Setup(service => service.ValidateAsync(
                It.IsAny<SubtitleQualityValidationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubtitleQualityValidationResult
            {
                IsValid = true,
                Summary = "ok",
                SourceEntryCount = 50,
                TargetEntryCount = 50,
                MinimumTargetEntryCount = 45
            });

        return qualityServiceMock;
    }

    private static string BuildSrtContent(int count)
    {
        return string.Join(
            Environment.NewLine + Environment.NewLine,
            Enumerable.Range(1, count).Select(index =>
            {
                var start = TimeSpan.FromSeconds(index);
                var end = TimeSpan.FromSeconds(index + 1);
                return $"{index}{Environment.NewLine}{start:hh\\:mm\\:ss},000 --> {end:hh\\:mm\\:ss},000{Environment.NewLine}Hello {index}";
            }));
    }

    private static string BuildExtractedAssContent(int streamIndex, int entries)
    {
        return $"{SubtitleExtractionService.ExtractionMarkerPrefix} StreamIndex={streamIndex}, Entries={entries}{Environment.NewLine}{Environment.NewLine}" +
               BuildAssContent(entries);
    }

    private static string BuildAssContent(int entries)
    {
        var dialogueLines = string.Join(
            Environment.NewLine,
            Enumerable.Range(1, entries)
                .Select(index => $"Dialogue: 0,0:00:{index % 60:00}.00,0:00:{(index % 60) + 1:00}.00,Default,,0,0,0,,Hello {index}"));

        return $"""
            [Script Info]
            Title: Example

            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
            Style: Default,Arial,28,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,2,2,10,10,10,1

            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            {dialogueLines}
            """;
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
