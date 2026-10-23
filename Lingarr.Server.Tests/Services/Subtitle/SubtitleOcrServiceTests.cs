using System;
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
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Subtitle;
using Lingarr.Server.Services.Subtitle;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SubtitleOcrServiceTests
{
    [Fact]
    public void NormalizeCommonOcrTextArtifacts_ReplacesStandalonePipeWithI()
    {
        var text = "When | was a boy,\n| haven't ignored it.\nA|B stays literal.";

        var normalized = SubtitleOcrService.NormalizeCommonOcrTextArtifacts(text);

        Assert.Equal("When I was a boy,\nI haven't ignored it.\nA|B stays literal.", normalized);
    }

    [Fact]
    public async Task QueueOcrAsync_WithExtensionlessFileNameContainingDots_QueuesOcr()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var fileName = "The Legend of Korra [DTS-HD MA 5.1]-FraMeSToR";
            await File.WriteAllTextAsync(Path.Combine(tempDirectory.FullName, fileName + ".mkv"), "media");

            await using var context = BuildContext();
            var show = new Show
            {
                SonarrId = 1,
                Title = "The Legend of Korra",
                Path = tempDirectory.FullName,
                DateAdded = DateTime.UtcNow
            };
            var season = new Season
            {
                SeasonNumber = 1,
                Path = tempDirectory.FullName,
                Show = show
            };
            var episode = new Episode
            {
                SonarrId = 10,
                EpisodeNumber = 1,
                Title = "Welcome to Republic City",
                Path = tempDirectory.FullName,
                FileName = fileName,
                Season = season
            };
            episode.EmbeddedSubtitles.Add(new EmbeddedSubtitle
            {
                StreamIndex = 0,
                Language = "eng",
                CodecName = "hdmv_pgs_subtitle",
                IsTextBased = false,
                Episode = episode
            });

            context.Shows.Add(show);
            context.Seasons.Add(season);
            context.Episodes.Add(episode);
            await context.SaveChangesAsync();

            var service = BuildService(context);

            var result = await service.QueueOcrAsync(episode.Id, MediaType.Episode, 0, manual: true);

            Assert.True(result.Success);
            Assert.Equal(SubtitleOcrStatus.Queued, result.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task QueueOcrAsync_WithBlockedLowQualityOutputAboveCurrentThreshold_PromotesOcr()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var fileName = "korra.s04e12.mkv";
            var mediaPath = Path.Combine(tempDirectory.FullName, fileName);
            var ocrPath = Path.Combine(tempDirectory.FullName, "korra.s04e12.eng.ocr.srt");
            await File.WriteAllTextAsync(mediaPath, "media");
            await File.WriteAllTextAsync(ocrPath, "1\n00:00:01,000 --> 00:00:02,000\nHello\n");

            await using var context = BuildContext();
            var movie = new Movie
            {
                RadarrId = 12,
                Title = "Korra S04E12",
                Path = tempDirectory.FullName,
                FileName = fileName,
                DateAdded = DateTime.UtcNow
            };
            movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
            {
                StreamIndex = 2,
                Language = "eng",
                Title = "English PGS",
                CodecName = "hdmv_pgs_subtitle",
                IsTextBased = false,
                OcrStatus = SubtitleOcrStatus.BlockedLowQuality,
                OcrExtractedPath = ocrPath,
                OcrQualityScore = 75,
                OcrIssueSummary = "15 % of cues are empty",
                OcrError = "15 % of cues are empty",
                Movie = movie
            });

            context.Movies.Add(movie);
            await context.SaveChangesAsync();

            var mediaState = new Mock<IMediaStateService>();
            mediaState
                .Setup(service => service.UpdateStateAsync(movie, MediaType.Movie, true))
                .ReturnsAsync(TranslationState.OcrPending);

            var service = BuildService(
                context,
                Mock.Of<ISubtitleService>(),
                Mock.Of<IEmbeddedSubtitleCacheService>(),
                Mock.Of<ISubtitleOcrEngine>(),
                ocrMinQualityScore: "70",
                mediaStateService: mediaState.Object);

            var result = await service.QueueOcrAsync(movie.Id, MediaType.Movie, 2, manual: false);

            Assert.True(result.Success);
            Assert.Equal(SubtitleOcrStatus.Succeeded, result.Status);
            Assert.Equal(ocrPath, result.ExtractedPath);
            Assert.Equal(75, result.QualityScore);

            var updatedSubtitle = await context.EmbeddedSubtitles.SingleAsync();
            Assert.Equal(SubtitleOcrStatus.Succeeded, updatedSubtitle.OcrStatus);
            Assert.Null(updatedSubtitle.OcrError);
            Assert.NotNull(updatedSubtitle.OcrCompletedAt);
            mediaState.Verify(
                service => service.UpdateStateAsync(movie, MediaType.Movie, true),
                Times.Once);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RunOcrAsync_WhenEmbeddedSubtitleRowIsReplaced_CompletesCurrentRow()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        await using var connection = new SqliteConnection("Filename=:memory:");
        try
        {
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<LingarrDbContext>()
                .UseSqlite(connection)
                .Options;

            await using (var setupContext = new LingarrDbContext(options))
            {
                await setupContext.Database.EnsureCreatedAsync();

                var fileName = "korra.mkv";
                await File.WriteAllTextAsync(Path.Combine(tempDirectory.FullName, fileName), "media");

                var show = new Show
                {
                    SonarrId = 1,
                    Title = "The Legend of Korra",
                    Path = tempDirectory.FullName,
                    DateAdded = DateTime.UtcNow
                };
                var season = new Season
                {
                    SeasonNumber = 2,
                    Path = tempDirectory.FullName,
                    Show = show
                };
                var episode = new Episode
                {
                    SonarrId = 10,
                    EpisodeNumber = 12,
                    Title = "Harmonic Convergence",
                    Path = tempDirectory.FullName,
                    FileName = fileName,
                    Season = season
                };
                episode.EmbeddedSubtitles.Add(new EmbeddedSubtitle
                {
                    StreamIndex = 0,
                    Language = "eng",
                    Title = "English PGS",
                    CodecName = "hdmv_pgs_subtitle",
                    IsTextBased = false,
                    Episode = episode
                });

                setupContext.Shows.Add(show);
                setupContext.Seasons.Add(season);
                setupContext.Episodes.Add(episode);
                await setupContext.SaveChangesAsync();
            }

            var cache = new Mock<IEmbeddedSubtitleCacheService>();
            cache
                .Setup(service => service.GetOcrCachePath(
                    It.IsAny<int>(),
                    It.IsAny<MediaType>(),
                    It.IsAny<int>(),
                    It.IsAny<string?>()))
                .Returns(Path.Combine(tempDirectory.FullName, "ocr.srt"));

            var subtitleService = new Mock<ISubtitleService>();
            subtitleService
                .Setup(service => service.ReadSubtitles(It.IsAny<string>()))
                .ReturnsAsync(Enumerable.Range(1, SubtitleExtractionService.MinimumDialogueEntries)
                    .Select(index => new SubtitleItem
                    {
                        Position = index,
                        StartTime = index * 1000,
                        EndTime = index * 1000 + 500,
                        Lines = [$"Line {index}"],
                        PlaintextLines = [$"Line {index}"]
                    })
                    .ToList());

            var engine = new Mock<ISubtitleOcrEngine>();
            engine
                .Setup(service => service.ConvertAsync(
                    It.IsAny<string>(),
                    0,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (string _, int _, string outputPath, string _, CancellationToken _) =>
                {
                    await using var replacementContext = new LingarrDbContext(options);
                    var existing = await replacementContext.EmbeddedSubtitles
                        .SingleAsync(subtitle => subtitle.StreamIndex == 0);
                    var episodeId = existing.EpisodeId;

                    replacementContext.EmbeddedSubtitles.Remove(existing);
                    await replacementContext.SaveChangesAsync();

                    replacementContext.EmbeddedSubtitles.Add(new EmbeddedSubtitle
                    {
                        EpisodeId = episodeId,
                        StreamIndex = 0,
                        Language = "eng",
                        Title = "English PGS",
                        CodecName = "hdmv_pgs_subtitle",
                        IsTextBased = false,
                        OcrStatus = SubtitleOcrStatus.Processing,
                        OcrAttemptedAt = DateTime.UtcNow
                    });
                    await replacementContext.SaveChangesAsync();

                    await File.WriteAllTextAsync(outputPath, "1\n00:00:01,000 --> 00:00:02,000\nHello\n");
                    return new SubtitleOcrEngineResult
                    {
                        Success = true,
                        OutputPath = outputPath
                    };
                });

            await using var context = new LingarrDbContext(options);
            var episodeId = await context.Episodes
                .Where(episode => episode.Title == "Harmonic Convergence")
                .Select(episode => episode.Id)
                .SingleAsync();
            var service = BuildService(
                context,
                subtitleService.Object,
                cache.Object,
                engine.Object);

            var result = await service.RunOcrAsync(episodeId, MediaType.Episode, 0, manual: false);

            Assert.True(result.Success);
            Assert.Equal(SubtitleOcrStatus.Succeeded, result.Status);

            await using var verifyContext = new LingarrDbContext(options);
            var currentSubtitle = await verifyContext.EmbeddedSubtitles.SingleAsync();
            Assert.Equal(SubtitleOcrStatus.Succeeded, currentSubtitle.OcrStatus);
            Assert.NotNull(currentSubtitle.OcrCompletedAt);
            Assert.Equal(SubtitleExtractionService.MinimumDialogueEntries, currentSubtitle.OcrCueCount);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RunOcrAsync_ConcurrentCallsForSameOutputPath_SerializeEngineExecution()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        await using var connection = new SqliteConnection("Filename=:memory:");
        try
        {
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<LingarrDbContext>()
                .UseSqlite(connection)
                .Options;

            await using (var setupContext = new LingarrDbContext(options))
            {
                await setupContext.Database.EnsureCreatedAsync();

                var fileName = "korra.mkv";
                await File.WriteAllTextAsync(Path.Combine(tempDirectory.FullName, fileName), "media");

                var show = new Show
                {
                    SonarrId = 1,
                    Title = "The Legend of Korra",
                    Path = tempDirectory.FullName,
                    DateAdded = DateTime.UtcNow
                };
                var season = new Season
                {
                    SeasonNumber = 4,
                    Path = tempDirectory.FullName,
                    Show = show
                };
                var episode = new Episode
                {
                    SonarrId = 10,
                    EpisodeNumber = 1,
                    Title = "Concurrent OCR",
                    Path = tempDirectory.FullName,
                    FileName = fileName,
                    Season = season
                };
                episode.EmbeddedSubtitles.Add(new EmbeddedSubtitle
                {
                    StreamIndex = 0,
                    Language = "eng",
                    Title = "English PGS",
                    CodecName = "hdmv_pgs_subtitle",
                    IsTextBased = false,
                    Episode = episode
                });

                setupContext.Shows.Add(show);
                setupContext.Seasons.Add(season);
                setupContext.Episodes.Add(episode);
                await setupContext.SaveChangesAsync();
            }

            var ocrPath = Path.Combine(tempDirectory.FullName, "ocr.srt");
            var gateArrivals = 0;
            var cache = new Mock<IEmbeddedSubtitleCacheService>();
            cache
                .Setup(service => service.GetOcrCachePath(
                    It.IsAny<int>(),
                    It.IsAny<MediaType>(),
                    It.IsAny<int>(),
                    It.IsAny<string?>()))
                .Returns((int _, MediaType _, int _, string? _) =>
                {
                    Interlocked.Increment(ref gateArrivals);
                    return ocrPath;
                });

            var subtitleService = new Mock<ISubtitleService>();
            subtitleService
                .Setup(service => service.ReadSubtitles(It.IsAny<string>()))
                .ReturnsAsync(Enumerable.Range(1, SubtitleExtractionService.MinimumDialogueEntries)
                    .Select(index => new SubtitleItem
                    {
                        Position = index,
                        StartTime = index * 1000,
                        EndTime = index * 1000 + 500,
                        Lines = [$"Line {index}"],
                        PlaintextLines = [$"Line {index}"]
                    })
                    .ToList());

            var engineCalls = 0;
            var engineConcurrency = 0;
            var maxEngineConcurrency = 0;
            var trackingLock = new object();
            var releaseFirstExecution = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var engine = new Mock<ISubtitleOcrEngine>();
            engine
                .Setup(service => service.ConvertAsync(
                    It.IsAny<string>(),
                    0,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (string _, int _, string outputPath, string _, CancellationToken _) =>
                {
                    int callNumber;
                    lock (trackingLock)
                    {
                        callNumber = ++engineCalls;
                        engineConcurrency++;
                        if (engineConcurrency > maxEngineConcurrency)
                        {
                            maxEngineConcurrency = engineConcurrency;
                        }
                    }

                    try
                    {
                        if (callNumber == 1)
                        {
                            // Hold the first engine execution until the second call has
                            // reached the serialization gate.
                            await releaseFirstExecution.Task;
                        }

                        await File.WriteAllTextAsync(
                            outputPath,
                            "1\n00:00:01,000 --> 00:00:02,000\nHello\n");
                        return new SubtitleOcrEngineResult
                        {
                            Success = true,
                            OutputPath = outputPath
                        };
                    }
                    finally
                    {
                        lock (trackingLock)
                        {
                            engineConcurrency--;
                        }
                    }
                });

            await using var context = new LingarrDbContext(options);
            var episodeId = await context.Episodes
                .Where(episode => episode.Title == "Concurrent OCR")
                .Select(episode => episode.Id)
                .SingleAsync();
            var service = BuildService(
                context,
                subtitleService.Object,
                cache.Object,
                engine.Object);

            var firstCall = service.RunOcrAsync(episodeId, MediaType.Episode, 0, manual: false);
            try
            {
                await WaitUntilAsync(
                    () => Volatile.Read(ref engineCalls) == 1,
                    TimeSpan.FromSeconds(5));

                var secondCall = service.RunOcrAsync(episodeId, MediaType.Episode, 0, manual: false);
                await WaitUntilAsync(
                    () => Volatile.Read(ref gateArrivals) == 2,
                    TimeSpan.FromSeconds(5));

                // The second call has computed its output path but must still be queued
                // on the semaphore: the engine may not be entered a second time while the
                // first execution is still running.
                Assert.Equal(1, Volatile.Read(ref engineCalls));
                Assert.Equal(1, Volatile.Read(ref maxEngineConcurrency));

                releaseFirstExecution.SetResult();

                var results = await Task.WhenAll(firstCall, secondCall);

                Assert.Equal(2, Volatile.Read(ref engineCalls));
                Assert.Equal(1, Volatile.Read(ref maxEngineConcurrency));
                Assert.All(results, result => Assert.Equal(SubtitleOcrStatus.Succeeded, result.Status));
            }
            finally
            {
                releaseFirstExecution.TrySetResult();
            }

            await using var verifyContext = new LingarrDbContext(options);
            var current = await verifyContext.EmbeddedSubtitles.AsNoTracking().SingleAsync();
            Assert.Equal(SubtitleOcrStatus.Succeeded, current.OcrStatus);
            Assert.Equal(SubtitleExtractionService.MinimumDialogueEntries, current.OcrCueCount);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task QueueOcrAsync_WithUnsupportedStreamLanguage_MarksFailedWithoutRunningEngine()
    {
        var (context, episodeId, tempDirectory) = await CreateEpisodeWithEmbeddedSubtitleAsync("chi");
        try
        {
            await using var db = context;
            var engine = new Mock<ISubtitleOcrEngine>();
            var mediaState = new Mock<IMediaStateService>();
            var ocrService = BuildService(
                db,
                Mock.Of<ISubtitleService>(),
                Mock.Of<IEmbeddedSubtitleCacheService>(),
                engine.Object,
                mediaStateService: mediaState.Object);

            var result = await ocrService.QueueOcrAsync(episodeId, MediaType.Episode, 0, manual: true);

            Assert.False(result.Success);
            Assert.Equal(SubtitleOcrStatus.Failed, result.Status);
            Assert.Contains("'chi' is not supported", result.Error!);
            Assert.Contains("no Tesseract language mapping", result.Error!);

            var subtitle = await db.EmbeddedSubtitles.SingleAsync();
            Assert.Equal(SubtitleOcrStatus.Failed, subtitle.OcrStatus);
            Assert.Contains("'chi' is not supported", subtitle.OcrError!);
            Assert.NotNull(subtitle.OcrCompletedAt);

            engine.Verify(
                ocrEngine => ocrEngine.ConvertAsync(
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            mediaState.Verify(
                state => state.UpdateStateAsync(
                    It.IsAny<IMedia>(),
                    It.IsAny<MediaType>(),
                    It.IsAny<bool>()),
                Times.Once);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RunOcrAsync_WithUnsupportedStreamLanguage_MarksFailedWithoutRunningEngine()
    {
        var (context, episodeId, tempDirectory) = await CreateEpisodeWithEmbeddedSubtitleAsync("chi");
        try
        {
            await using var db = context;
            var engine = new Mock<ISubtitleOcrEngine>();
            var mediaState = new Mock<IMediaStateService>();
            var ocrService = BuildService(
                db,
                Mock.Of<ISubtitleService>(),
                Mock.Of<IEmbeddedSubtitleCacheService>(),
                engine.Object,
                mediaStateService: mediaState.Object);

            var result = await ocrService.RunOcrAsync(episodeId, MediaType.Episode, 0, manual: false);

            Assert.False(result.Success);
            Assert.Equal(SubtitleOcrStatus.Failed, result.Status);
            Assert.Contains("'chi' is not supported", result.Error!);

            var subtitle = await db.EmbeddedSubtitles.SingleAsync();
            Assert.Equal(SubtitleOcrStatus.Failed, subtitle.OcrStatus);
            Assert.Contains("no Tesseract language mapping", subtitle.OcrError!);

            engine.Verify(
                ocrEngine => ocrEngine.ConvertAsync(
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            mediaState.Verify(
                state => state.UpdateStateAsync(
                    It.IsAny<IMedia>(),
                    It.IsAny<MediaType>(),
                    It.IsAny<bool>()),
                Times.Once);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task QueueOcrAsync_WithUsableSucceededOcrAndUnsupportedLanguage_PreservesUsableResult()
    {
        var (context, episodeId, tempDirectory) = await CreateEpisodeWithEmbeddedSubtitleAsync("chi");
        try
        {
            await using var db = context;
            var cachedOcrPath = await MarkSubtitleAsUsableSucceededOcrAsync(db, tempDirectory);
            var engine = new Mock<ISubtitleOcrEngine>();
            var mediaState = new Mock<IMediaStateService>();
            var ocrService = BuildService(
                db,
                Mock.Of<ISubtitleService>(),
                Mock.Of<IEmbeddedSubtitleCacheService>(),
                engine.Object,
                mediaStateService: mediaState.Object);

            var result = await ocrService.QueueOcrAsync(episodeId, MediaType.Episode, 0, manual: true);

            VerifyUsableResultPreserved(result, cachedOcrPath);
            var current = await db.EmbeddedSubtitles.SingleAsync();
            Assert.Equal(SubtitleOcrStatus.Succeeded, current.OcrStatus);
            Assert.Equal(cachedOcrPath, current.OcrExtractedPath);
            Assert.Equal(100, current.OcrQualityScore);
            Assert.Null(current.OcrError);

            engine.Verify(
                ocrEngine => ocrEngine.ConvertAsync(
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            mediaState.Verify(
                state => state.UpdateStateAsync(
                    It.IsAny<IMedia>(),
                    It.IsAny<MediaType>(),
                    It.IsAny<bool>()),
                Times.Never);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RunOcrAsync_WithUsableSucceededOcrAndUnsupportedLanguage_PreservesUsableResult()
    {
        var (context, episodeId, tempDirectory) = await CreateEpisodeWithEmbeddedSubtitleAsync("chi");
        try
        {
            await using var db = context;
            var cachedOcrPath = await MarkSubtitleAsUsableSucceededOcrAsync(db, tempDirectory);
            var engine = new Mock<ISubtitleOcrEngine>();
            var mediaState = new Mock<IMediaStateService>();
            var ocrService = BuildService(
                db,
                Mock.Of<ISubtitleService>(),
                Mock.Of<IEmbeddedSubtitleCacheService>(),
                engine.Object,
                mediaStateService: mediaState.Object);

            var result = await ocrService.RunOcrAsync(episodeId, MediaType.Episode, 0, manual: false);

            VerifyUsableResultPreserved(result, cachedOcrPath);
            var current = await db.EmbeddedSubtitles.SingleAsync();
            Assert.Equal(SubtitleOcrStatus.Succeeded, current.OcrStatus);
            Assert.Equal(cachedOcrPath, current.OcrExtractedPath);
            Assert.Equal(100, current.OcrQualityScore);
            Assert.Null(current.OcrError);

            engine.Verify(
                ocrEngine => ocrEngine.ConvertAsync(
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            mediaState.Verify(
                state => state.UpdateStateAsync(
                    It.IsAny<IMedia>(),
                    It.IsAny<MediaType>(),
                    It.IsAny<bool>()),
                Times.Never);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task QueueOcrAsync_WithUntaggedStreamLanguage_QueuesWithEnglishFallback()
    {
        var (context, episodeId, tempDirectory) = await CreateEpisodeWithEmbeddedSubtitleAsync(string.Empty);
        try
        {
            await using var db = context;
            var ocrService = BuildService(
                db,
                Mock.Of<ISubtitleService>(),
                Mock.Of<IEmbeddedSubtitleCacheService>(),
                Mock.Of<ISubtitleOcrEngine>());

            var result = await ocrService.QueueOcrAsync(episodeId, MediaType.Episode, 0, manual: true);

            Assert.True(result.Success);
            Assert.Equal(SubtitleOcrStatus.Queued, result.Status);

            var subtitle = await db.EmbeddedSubtitles.SingleAsync();
            Assert.Equal(SubtitleOcrStatus.Queued, subtitle.OcrStatus);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task QueueOcrAsync_WithUndLanguage_QueuesWithEnglishFallback()
    {
        var (context, episodeId, tempDirectory) = await CreateEpisodeWithEmbeddedSubtitleAsync("und");
        try
        {
            await using var db = context;
            var ocrService = BuildService(
                db,
                Mock.Of<ISubtitleService>(),
                Mock.Of<IEmbeddedSubtitleCacheService>(),
                Mock.Of<ISubtitleOcrEngine>());

            var result = await ocrService.QueueOcrAsync(episodeId, MediaType.Episode, 0, manual: true);

            Assert.True(result.Success);
            Assert.Equal(SubtitleOcrStatus.Queued, result.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RunOcrAsync_WithUntaggedStreamLanguage_UsesEnglishTraineddata()
    {
        var (context, episodeId, tempDirectory) = await CreateEpisodeWithEmbeddedSubtitleAsync(string.Empty);
        try
        {
            await using var db = context;
            var outputPath = Path.Combine(tempDirectory.FullName, "ocr.srt");
            var cache = new Mock<IEmbeddedSubtitleCacheService>();
            cache
                .Setup(service => service.GetOcrCachePath(
                    It.IsAny<int>(),
                    It.IsAny<MediaType>(),
                    It.IsAny<int>(),
                    It.IsAny<string?>()))
                .Returns(outputPath);

            string? usedLanguage = null;
            var engine = new Mock<ISubtitleOcrEngine>();
            engine
                .Setup(service => service.ConvertAsync(
                    It.IsAny<string>(),
                    0,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns((string _, int _, string _, string tesseractLanguage, CancellationToken _) =>
                {
                    usedLanguage = tesseractLanguage;
                    return Task.FromResult(new SubtitleOcrEngineResult
                    {
                        Success = false,
                        Error = "engine stub"
                    });
                });

            var ocrService = BuildService(
                db,
                Mock.Of<ISubtitleService>(),
                cache.Object,
                engine.Object);

            var result = await ocrService.RunOcrAsync(episodeId, MediaType.Episode, 0, manual: false);

            Assert.Equal("eng", usedLanguage);
            Assert.False(result.Success);
            Assert.Equal("engine stub", result.Error);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task QueueOcrAsync_WithUnsupportedOcrLanguageSetting_MarksFailed()
    {
        var (context, episodeId, tempDirectory) = await CreateEpisodeWithEmbeddedSubtitleAsync("eng");
        try
        {
            await using var db = context;
            var ocrService = BuildService(
                db,
                Mock.Of<ISubtitleService>(),
                Mock.Of<IEmbeddedSubtitleCacheService>(),
                Mock.Of<ISubtitleOcrEngine>(),
                ocrLanguages: "chi");

            var result = await ocrService.QueueOcrAsync(episodeId, MediaType.Episode, 0, manual: true);

            Assert.False(result.Success);
            Assert.Equal(SubtitleOcrStatus.Failed, result.Status);
            Assert.Contains("'chi' is not supported", result.Error!);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task QueueOcrAsync_WithExplicitSupportedOcrLanguage_QueuesForeignStream()
    {
        var (context, episodeId, tempDirectory) = await CreateEpisodeWithEmbeddedSubtitleAsync("chi");
        try
        {
            await using var db = context;
            var ocrService = BuildService(
                db,
                Mock.Of<ISubtitleService>(),
                Mock.Of<IEmbeddedSubtitleCacheService>(),
                Mock.Of<ISubtitleOcrEngine>(),
                ocrLanguages: "eng");

            var result = await ocrService.QueueOcrAsync(episodeId, MediaType.Episode, 0, manual: true);

            Assert.True(result.Success);
            Assert.Equal(SubtitleOcrStatus.Queued, result.Status);

            var subtitle = await db.EmbeddedSubtitles.SingleAsync();
            Assert.Equal(SubtitleOcrStatus.Queued, subtitle.OcrStatus);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task GetPreviewAsync_WithStaleManagedOcrOutput_DoesNotInvalidateOrResetState()
    {
        var (context, episodeId, tempDirectory) = await CreateEpisodeWithEmbeddedSubtitleAsync("eng");
        try
        {
            var cachePath = Path.Combine(tempDirectory.FullName, "cache", "episode.ocr.srt");
            var subtitle = await context.EmbeddedSubtitles.SingleAsync();
            subtitle.OcrStatus = SubtitleOcrStatus.BlockedLowQuality;
            subtitle.OcrQualityScore = 75;
            subtitle.OcrIssueSummary = "Quality score 75 is below the required 80.";
            subtitle.OcrExtractedPath = cachePath;
            subtitle.OcrCompletedAt = new DateTime(2026, 5, 10, 21, 16, 50, DateTimeKind.Utc);
            await context.SaveChangesAsync();

            var cache = new Mock<IEmbeddedSubtitleCacheService>();
            cache.Setup(service => service.IsManagedCachePath(It.IsAny<string?>())).Returns(true);
            cache
                .Setup(service => service.IsCurrentForSource(
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .Returns(false);

            var service = BuildService(
                context,
                Mock.Of<ISubtitleService>(),
                cache.Object,
                Mock.Of<ISubtitleOcrEngine>());

            var preview = await service.GetPreviewAsync(episodeId, MediaType.Episode, 0);

            Assert.False(preview.Success);
            cache.Verify(service => service.Invalidate(It.IsAny<string>()), Times.Never);
            var currentSubtitle = await context.EmbeddedSubtitles.SingleAsync();
            Assert.Equal(SubtitleOcrStatus.BlockedLowQuality, currentSubtitle.OcrStatus);
            Assert.Equal(cachePath, currentSubtitle.OcrExtractedPath);
            Assert.Equal(75, currentSubtitle.OcrQualityScore);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task QueueOcrAsync_WithStaleManagedOcrOutput_StillInvalidatesCachedOutput()
    {
        var (context, episodeId, tempDirectory) = await CreateEpisodeWithEmbeddedSubtitleAsync("eng");
        try
        {
            var cachePath = Path.Combine(tempDirectory.FullName, "cache", "episode.ocr.srt");
            var subtitle = await context.EmbeddedSubtitles.SingleAsync();
            subtitle.OcrStatus = SubtitleOcrStatus.BlockedLowQuality;
            subtitle.OcrQualityScore = 75;
            subtitle.OcrIssueSummary = "Quality score 75 is below the required 80.";
            subtitle.OcrExtractedPath = cachePath;
            await context.SaveChangesAsync();

            var cache = new Mock<IEmbeddedSubtitleCacheService>();
            cache.Setup(service => service.IsManagedCachePath(It.IsAny<string?>())).Returns(true);
            cache
                .Setup(service => service.IsCurrentForSource(
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .Returns(false);

            var service = BuildService(
                context,
                Mock.Of<ISubtitleService>(),
                cache.Object,
                Mock.Of<ISubtitleOcrEngine>());

            await service.QueueOcrAsync(episodeId, MediaType.Episode, 0, manual: true);

            cache.Verify(service => service.Invalidate(cachePath), Times.Once);
            var currentSubtitle = await context.EmbeddedSubtitles.SingleAsync();
            Assert.Equal(SubtitleOcrStatus.Queued, currentSubtitle.OcrStatus);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    private static async Task<string> MarkSubtitleAsUsableSucceededOcrAsync(
        LingarrDbContext db,
        DirectoryInfo tempDirectory)
    {
        var cachedOcrPath = Path.Combine(tempDirectory.FullName, "episode-0.ocr.srt");
        await File.WriteAllTextAsync(
            cachedOcrPath,
            "1\n00:00:01,000 --> 00:00:02,000\nReadable dialogue.\n");

        var subtitle = await db.EmbeddedSubtitles.SingleAsync();
        subtitle.OcrStatus = SubtitleOcrStatus.Succeeded;
        subtitle.OcrExtractedPath = cachedOcrPath;
        subtitle.OcrCueCount = SubtitleExtractionService.MinimumDialogueEntries;
        subtitle.OcrQualityScore = 100;
        subtitle.OcrIssueSummary = "OCR output passed quality checks.";
        subtitle.OcrCompletedAt = new DateTime(2026, 5, 10, 21, 0, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();
        return cachedOcrPath;
    }

    private static void VerifyUsableResultPreserved(SubtitleOcrResult result, string cachedOcrPath)
    {
        Assert.True(result.Success);
        Assert.Equal(SubtitleOcrStatus.Succeeded, result.Status);
        Assert.Equal(cachedOcrPath, result.ExtractedPath);
        Assert.Equal(100, result.QualityScore);
        Assert.Null(result.Error);
    }

    private static async Task<(LingarrDbContext Context, int EpisodeId, DirectoryInfo TempDirectory)>
        CreateEpisodeWithEmbeddedSubtitleAsync(string streamLanguage)
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var fileName = "fallout.s01e01";
        await File.WriteAllTextAsync(Path.Combine(tempDirectory.FullName, fileName + ".mkv"), "media");

        var context = BuildContext();
        var show = new Show
        {
            SonarrId = 1,
            Title = "Fallout",
            Path = tempDirectory.FullName,
            DateAdded = DateTime.UtcNow
        };
        var season = new Season
        {
            SeasonNumber = 1,
            Path = tempDirectory.FullName,
            Show = show
        };
        var episode = new Episode
        {
            SonarrId = 1,
            EpisodeNumber = 1,
            Title = "The End",
            Path = tempDirectory.FullName,
            FileName = fileName,
            Season = season
        };
        episode.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            StreamIndex = 0,
            Language = streamLanguage,
            Title = "PGS",
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            Episode = episode
        });

        context.Shows.Add(show);
        context.Seasons.Add(season);
        context.Episodes.Add(episode);
        await context.SaveChangesAsync();

        return (context, episode.Id, tempDirectory);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"Condition was not met within {timeout}.");
            }

            await Task.Delay(25);
        }
    }

    private static LingarrDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new LingarrDbContext(options);
    }

    private static SubtitleOcrService BuildService(LingarrDbContext context)
    {
        return BuildService(
            context,
            Mock.Of<ISubtitleService>(),
            Mock.Of<IEmbeddedSubtitleCacheService>(),
            Mock.Of<ISubtitleOcrEngine>());
    }

    private static SubtitleOcrService BuildService(
        LingarrDbContext context,
        ISubtitleService subtitleService,
        IEmbeddedSubtitleCacheService cacheService,
        ISubtitleOcrEngine ocrEngine,
        string ocrMinQualityScore = "80",
        IMediaStateService? mediaStateService = null,
        string ocrLanguages = "auto")
    {
        var settings = new Mock<ISettingService>();
        settings
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrEnabled))
            .ReturnsAsync("true");
        settings
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrMinQualityScore))
            .ReturnsAsync(ocrMinQualityScore);
        settings
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrLanguages))
            .ReturnsAsync(ocrLanguages);

        return new SubtitleOcrService(
            context,
            settings.Object,
            subtitleService,
            cacheService,
            ocrEngine,
            mediaStateService ?? Mock.Of<IMediaStateService>(),
            NullLogger<SubtitleOcrService>.Instance);
    }
}
