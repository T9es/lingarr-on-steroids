using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Integration;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.Integrations;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Sync;

public class EpisodeSyncTests
{
    [Fact]
    public async Task SyncEpisodes_WhenSonarrReportsNoFile_StampsMissingSince()
    {
        await using var context = BuildContext();
        var (season, episode) = await SeedEpisodeAsync(context);
        var sonarrServiceMock = new Mock<ISonarrService>();
        sonarrServiceMock
            .Setup(service => service.GetEpisodes(ShowId, SeasonNumber, InstanceUrl, InstanceApiKey))
            .ReturnsAsync([CreateSonarrEpisode(episode.SonarrId, hasFile: false)]);
        var service = CreateService(context, sonarrServiceMock);

        await service.SyncEpisodes(CreateSonarrShow(), season, InstanceId, InstanceUrl, InstanceApiKey);
        await context.SaveChangesAsync();

        Assert.NotNull(episode.MissingSince);
    }

    [Fact]
    public async Task SyncEpisodes_WhenEpisodeStaysMissing_PreservesFirstMissingSince()
    {
        await using var context = BuildContext();
        var firstObserved = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        var (season, episode) = await SeedEpisodeAsync(context, firstObserved);
        var sonarrServiceMock = new Mock<ISonarrService>();
        sonarrServiceMock
            .Setup(service => service.GetEpisodes(ShowId, SeasonNumber, InstanceUrl, InstanceApiKey))
            .ReturnsAsync([CreateSonarrEpisode(episode.SonarrId, hasFile: false)]);
        var service = CreateService(context, sonarrServiceMock);

        await service.SyncEpisodes(CreateSonarrShow(), season, InstanceId, InstanceUrl, InstanceApiKey);
        await context.SaveChangesAsync();

        Assert.Equal(firstObserved, episode.MissingSince);
    }

    [Fact]
    public async Task SyncEpisodes_WhenEpisodeIsNoLongerListedBySonarr_StampsMissingSinceAndKeepsRow()
    {
        await using var context = BuildContext();
        var (season, episode) = await SeedEpisodeAsync(context);
        var sonarrServiceMock = new Mock<ISonarrService>();
        sonarrServiceMock
            .Setup(service => service.GetEpisodes(ShowId, SeasonNumber, InstanceUrl, InstanceApiKey))
            .ReturnsAsync([]);
        var service = CreateService(context, sonarrServiceMock);

        await service.SyncEpisodes(CreateSonarrShow(), season, InstanceId, InstanceUrl, InstanceApiKey);
        await context.SaveChangesAsync();

        Assert.Equal(1, await context.Episodes.CountAsync());
        Assert.NotNull((await context.Episodes.SingleAsync()).MissingSince);
    }

    [Fact]
    public async Task SyncEpisodes_WhenEpisodeIsNoLongerListedBySonarr_ClearsStaleEmbeddedSubtitles()
    {
        await using var context = BuildContext();
        var (season, episode) = await SeedEpisodeAsync(context);
        episode.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            StreamIndex = 0,
            Language = "eng",
            CodecName = "subrip",
            IsTextBased = true
        });
        await context.SaveChangesAsync();
        var sonarrServiceMock = new Mock<ISonarrService>();
        sonarrServiceMock
            .Setup(service => service.GetEpisodes(ShowId, SeasonNumber, InstanceUrl, InstanceApiKey))
            .ReturnsAsync([]);
        var service = CreateService(context, sonarrServiceMock);

        await service.SyncEpisodes(CreateSonarrShow(), season, InstanceId, InstanceUrl, InstanceApiKey);
        await context.SaveChangesAsync();

        Assert.NotNull(episode.MissingSince);
        Assert.Equal(0, await context.EmbeddedSubtitles.CountAsync());
    }

    [Fact]
    public async Task SyncEpisodes_WhenFilelessEpisodeStaysInSonarr_KeepsEmbeddedSubtitlesAndDoesNotStamp()
    {
        await using var context = BuildContext();
        var (season, episode) = await SeedEpisodeAsync(context);
        episode.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            StreamIndex = 0,
            Language = "eng",
            CodecName = "subrip",
            IsTextBased = true
        });
        await context.SaveChangesAsync();
        var sonarrServiceMock = new Mock<ISonarrService>();
        sonarrServiceMock
            .Setup(service => service.GetEpisodes(ShowId, SeasonNumber, InstanceUrl, InstanceApiKey))
            .ReturnsAsync([CreateSonarrEpisode(episode.SonarrId, hasFile: false)]);
        var service = CreateService(context, sonarrServiceMock);

        await service.SyncEpisodes(CreateSonarrShow(), season, InstanceId, InstanceUrl, InstanceApiKey);
        await context.SaveChangesAsync();

        Assert.Null(episode.MissingSince);
        Assert.Equal(1, await context.EmbeddedSubtitles.CountAsync());
    }

    [Fact]
    public async Task SyncEpisodes_WhenEpisodeHasTranslationHistory_DoesNotStampMissingSince()
    {
        await using var context = BuildContext();
        var (season, episode) = await SeedEpisodeAsync(context);
        context.TranslationRequests.Add(new TranslationRequest
        {
            MediaId = episode.Id,
            MediaType = MediaType.Episode,
            WorkloadKind = TranslationWorkloadKind.Library,
            Title = episode.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            Status = TranslationStatus.Completed
        });
        await context.SaveChangesAsync();
        var sonarrServiceMock = new Mock<ISonarrService>();
        sonarrServiceMock
            .Setup(service => service.GetEpisodes(ShowId, SeasonNumber, InstanceUrl, InstanceApiKey))
            .ReturnsAsync([CreateSonarrEpisode(episode.SonarrId, hasFile: false)]);
        var service = CreateService(context, sonarrServiceMock);

        await service.SyncEpisodes(CreateSonarrShow(), season, InstanceId, InstanceUrl, InstanceApiKey);
        await context.SaveChangesAsync();

        Assert.Null(episode.MissingSince);
    }

    [Fact]
    public async Task SyncEpisodes_WhenFileReturns_ClearsMissingSince()
    {
        await using var context = BuildContext();
        var (season, episode) = await SeedEpisodeAsync(
            context,
            missingSince: new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc));
        var sonarrServiceMock = new Mock<ISonarrService>();
        sonarrServiceMock
            .Setup(service => service.GetEpisodes(ShowId, SeasonNumber, InstanceUrl, InstanceApiKey))
            .ReturnsAsync([CreateSonarrEpisode(episode.SonarrId, hasFile: true)]);
        sonarrServiceMock
            .Setup(service => service.GetEpisodePath(episode.SonarrId, InstanceUrl, InstanceApiKey))
            .ReturnsAsync(new SonarrEpisodePath
            {
                EpisodeFile = new SonarrEpisodeFile
                {
                    Path = "/shows/show/Season 1/show.s01e01.mkv",
                    DateAdded = DateTime.UtcNow
                }
            });
        var service = CreateService(context, sonarrServiceMock);

        await service.SyncEpisodes(CreateSonarrShow(), season, InstanceId, InstanceUrl, InstanceApiKey);
        await context.SaveChangesAsync();

        Assert.Null(episode.MissingSince);
    }

    private const int ShowId = 1;
    private const int SeasonNumber = 1;
    private const string InstanceId = "default";
    private const string InstanceUrl = "http://sonarr:8989";
    private const string InstanceApiKey = "api-key";

    private static EpisodeSync CreateService(
        LingarrDbContext context,
        Mock<ISonarrService> sonarrServiceMock)
    {
        var mediaStateServiceMock = new Mock<IMediaStateService>();
        mediaStateServiceMock
            .Setup(service => service.UpdateStateAsync(
                It.IsAny<IMedia>(),
                It.IsAny<MediaType>(),
                It.IsAny<bool>()))
            .ReturnsAsync(TranslationState.AwaitingSource);

        return new EpisodeSync(
            sonarrServiceMock.Object,
            new PathConversionService(context),
            Mock.Of<ISubtitleExtractionService>(),
            mediaStateServiceMock.Object,
            Mock.Of<IOrphanSubtitleCleanupService>(),
            NullLogger<EpisodeSync>.Instance,
            context);
    }

    private static async Task<(Season Season, Episode Episode)> SeedEpisodeAsync(
        LingarrDbContext context,
        DateTime? missingSince = null)
    {
        var show = new Show
        {
            Id = ShowId,
            SonarrId = ShowId,
            Title = "Show",
            Path = "/shows/show",
            DateAdded = DateTime.UtcNow.AddDays(-10),
            SourceInstanceId = InstanceId
        };
        var season = new Season
        {
            SeasonNumber = SeasonNumber,
            Path = "/shows/show/Season 1",
            Show = show
        };
        var episode = new Episode
        {
            Id = 10,
            SonarrId = 10,
            EpisodeNumber = 1,
            Title = "Episode 1",
            FileName = "show.s01e01",
            Path = "/shows/show/Season 1",
            DateAdded = DateTime.UtcNow.AddDays(-10),
            Season = season,
            SourceInstanceId = InstanceId,
            TranslationState = TranslationState.AwaitingSource,
            MissingSince = missingSince
        };
        season.Episodes.Add(episode);
        show.Seasons.Add(season);
        context.Shows.Add(show);
        await context.SaveChangesAsync();
        return (season, episode);
    }

    private static SonarrShow CreateSonarrShow()
    {
        return new SonarrShow
        {
            Id = ShowId,
            Title = "Show",
            Path = "/shows/show",
            Added = DateTime.UtcNow.AddDays(-10).ToString("O"),
            SeasonFolder = true
        };
    }

    private static SonarrEpisode CreateSonarrEpisode(int id, bool hasFile)
    {
        return new SonarrEpisode
        {
            Id = id,
            EpisodeNumber = 1,
            Title = "Episode 1",
            SeasonNumber = SeasonNumber,
            HasFile = hasFile
        };
    }

    private static LingarrDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LingarrDbContext(options);
    }
}
