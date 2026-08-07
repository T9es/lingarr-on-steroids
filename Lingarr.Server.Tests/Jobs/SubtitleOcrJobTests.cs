using System;
using System.Reflection;
using System.Threading.Tasks;
using Hangfire;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models.Subtitle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Jobs;

public class SubtitleOcrJobTests
{
    [Fact]
    public void Execute_DisablesConcurrentOcrRuns()
    {
        var executeMethod = typeof(SubtitleOcrJob).GetMethod(nameof(SubtitleOcrJob.Execute));

        Assert.NotNull(executeMethod);
        Assert.NotNull(executeMethod.GetCustomAttribute<DisableConcurrentExecutionAttribute>());
    }

    [Fact]
    public async Task Execute_WhenOcrSucceeds_ImmediatelyRunsTranslationHandoff()
    {
        await using var context = BuildContext();
        var episode = AddEpisode(context);
        await context.SaveChangesAsync();
        var ocrService = new Mock<ISubtitleOcrService>();
        ocrService
            .Setup(service => service.RunOcrAsync(
                episode.Id,
                MediaType.Episode,
                0,
                false,
                default))
            .ReturnsAsync(new SubtitleOcrResult
            {
                Success = true,
                Status = SubtitleOcrStatus.Succeeded,
                CueCount = 100,
                QualityScore = 100
            });
        var mediaSubtitleProcessor = new Mock<IMediaSubtitleProcessor>();

        var job = new SubtitleOcrJob(
            ocrService.Object,
            mediaSubtitleProcessor.Object,
            context,
            NullLogger<SubtitleOcrJob>.Instance);

        await job.Execute(episode.Id, MediaType.Episode, 0, manual: false);

        mediaSubtitleProcessor.Verify(
            processor => processor.ProcessMediaForceAsync(
                It.Is<IMedia>(media => media.Id == episode.Id),
                MediaType.Episode,
                true,
                false,
                false,
                true,
                null,
                It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task Execute_WhenOcrIsBlocked_DoesNotRunTranslationHandoff()
    {
        await using var context = BuildContext();
        var episode = AddEpisode(context);
        await context.SaveChangesAsync();
        var ocrService = new Mock<ISubtitleOcrService>();
        ocrService
            .Setup(service => service.RunOcrAsync(
                episode.Id,
                MediaType.Episode,
                0,
                false,
                default))
            .ReturnsAsync(new SubtitleOcrResult
            {
                Success = false,
                Status = SubtitleOcrStatus.BlockedLowQuality,
                Error = "Quality score 75 is below the required 80."
            });
        var mediaSubtitleProcessor = new Mock<IMediaSubtitleProcessor>();

        var job = new SubtitleOcrJob(
            ocrService.Object,
            mediaSubtitleProcessor.Object,
            context,
            NullLogger<SubtitleOcrJob>.Instance);

        await job.Execute(episode.Id, MediaType.Episode, 0, manual: false);

        mediaSubtitleProcessor.Verify(
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

    private static LingarrDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new LingarrDbContext(options);
    }

    private static Episode AddEpisode(LingarrDbContext context)
    {
        var show = new Show
        {
            SonarrId = 1,
            Title = "The Legend of Korra",
            Path = "/media",
            DateAdded = DateTime.UtcNow
        };
        var season = new Season
        {
            SeasonNumber = 1,
            Path = "/media/season",
            Show = show
        };
        var episode = new Episode
        {
            SonarrId = 10,
            EpisodeNumber = 1,
            Title = "Welcome to Republic City",
            Path = "/media/season",
            FileName = "korra.mkv",
            Season = season
        };

        context.Shows.Add(show);
        context.Seasons.Add(season);
        context.Episodes.Add(episode);
        return episode;
    }
}
