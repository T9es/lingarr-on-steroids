using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
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
        ISubtitleOcrEngine ocrEngine)
    {
        var settings = new Mock<ISettingService>();
        settings
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrEnabled))
            .ReturnsAsync("true");
        settings
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrMinQualityScore))
            .ReturnsAsync("80");
        settings
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrLanguages))
            .ReturnsAsync("auto");

        return new SubtitleOcrService(
            context,
            settings.Object,
            subtitleService,
            cacheService,
            ocrEngine,
            Mock.Of<IMediaStateService>(),
            NullLogger<SubtitleOcrService>.Instance);
    }
}
