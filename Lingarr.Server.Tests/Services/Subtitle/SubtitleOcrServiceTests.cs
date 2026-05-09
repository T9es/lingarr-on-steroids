using System;
using System.IO;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Services.Subtitle;
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

    private static LingarrDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new LingarrDbContext(options);
    }

    private static SubtitleOcrService BuildService(LingarrDbContext context)
    {
        var settings = new Mock<ISettingService>();
        settings
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrEnabled))
            .ReturnsAsync("true");

        return new SubtitleOcrService(
            context,
            settings.Object,
            Mock.Of<ISubtitleService>(),
            Mock.Of<IEmbeddedSubtitleCacheService>(),
            Mock.Of<ISubtitleOcrEngine>(),
            Mock.Of<IMediaStateService>(),
            NullLogger<SubtitleOcrService>.Instance);
    }
}
