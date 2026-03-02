using System;
using System.Net;
using System.Threading.Tasks;
using System.Collections.Generic;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using System.Net.Http;
using Lingarr.Server.Interfaces.Services.Integration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Sync;
using Lingarr.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using System.Linq;

namespace Lingarr.Server.Tests.Services;

public class MediaServiceTests
{
    [Fact]
    public async Task GetEpisodeIdOrSyncFromSonarrEpisodeId_WhenEpisodeNotFound_TriesToResyncShows()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new LingarrDbContext(options);

        // Seed DB with no episodes
        await context.SaveChangesAsync();

        var sonarrMock = new Mock<ISonarrService>();
        var radarrMock = new Mock<IRadarrService>();
        var movieSyncMock = new Mock<IMovieSyncService>();
        var subtitleMock = new Mock<ISubtitleService>();
        var showSyncServiceMock = new Mock<IShowSyncService>();
        var mediaSubtitleProcessorMock = new Mock<IMediaSubtitleProcessor>();
        var settingServiceMock = new Mock<ISettingService>();
        var logger = NullLogger<MediaService>.Instance;

        // Configure setting service to return empty instance config and fallback credentials
        settingServiceMock
            .SetupSequence(s => s.GetSetting(It.IsAny<string>()))
            .ReturnsAsync(string.Empty)  // SonarrInstances - empty, use fallback
            .ReturnsAsync("http://test.sonarr.com")  // SonarrUrl
            .ReturnsAsync("test-api-key");  // SonarrApiKey

        // Configure Sonarr to throw HttpRequestException with 404 status for instance-aware overload
        sonarrMock
            .Setup(s => s.GetEpisode(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("Not found", null, HttpStatusCode.NotFound));

        // Configure GetShows to return a sample list when called
        sonarrMock
            .Setup(s => s.GetShows())
            .ReturnsAsync(new List<Lingarr.Server.Models.Integrations.SonarrShow>()
            {
                new Lingarr.Server.Models.Integrations.SonarrShow
                {
                    Id = 1,
                    Title = "Sample Show",
                    Path = "/tmp",
                    Added = System.DateTime.UtcNow.ToString("o"),
                    SeasonFolder = false,
                    Seasons = new List<Lingarr.Server.Models.Integrations.SonarrSeason>()
                }
            });

        var mediaService = new MediaService(context,
            subtitleMock.Object,
            sonarrMock.Object,
            showSyncServiceMock.Object,
            radarrMock.Object,
            movieSyncMock.Object,
            mediaSubtitleProcessorMock.Object,
            settingServiceMock.Object,
            logger);

        // Act
        var result = await mediaService.GetEpisodeIdOrSyncFromSonarrEpisodeId(150);

        // Assert
        sonarrMock.Verify(s => s.GetEpisode(150, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        sonarrMock.Verify(s => s.GetShows(), Times.Once);
        showSyncServiceMock.Verify(s => s.SyncShows(It.IsAny<List<(Lingarr.Server.Models.Integrations.SonarrShow Show, string InstanceId)>>()), Times.Once);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task GetShow_ReturnsSeasonsAndEpisodes()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new LingarrDbContext(options);

        var show = new Show
        {
            Id = 2,
            Title = "Detailed Show",
            SonarrId = 2,
            Path = "/tmp/show2",
            DateAdded = DateTime.UtcNow
        };

        var season = new Season
        {
            Id = 2,
            SeasonNumber = 1,
            Show = show
        };
        show.Seasons.Add(season);

        var episode = new Episode
        {
            Id = 2,
            EpisodeNumber = 1,
            Title = "Ep 1",
            SonarrId = 2,
            Season = season
        };
        season.Episodes.Add(episode);

        context.Shows.Add(show);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var sonarrMock = new Mock<ISonarrService>();
        var radarrMock = new Mock<IRadarrService>();
        var showSyncServiceMock = new Mock<IShowSyncService>();
        var movieSyncMock = new Mock<IMovieSyncService>();
        var subtitleMock = new Mock<ISubtitleService>();
        var mediaSubtitleProcessorMock = new Mock<IMediaSubtitleProcessor>();
        var settingServiceMock = new Mock<ISettingService>();
        var logger = NullLogger<MediaService>.Instance;

        var mediaService = new MediaService(context,
            subtitleMock.Object,
            sonarrMock.Object,
            showSyncServiceMock.Object,
            radarrMock.Object,
            movieSyncMock.Object,
            mediaSubtitleProcessorMock.Object,
            settingServiceMock.Object,
            logger);

        // Act
        var result = await mediaService.GetShow(2);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Detailed Show", result!.Title);
        Assert.Single(result.Seasons);
        Assert.Single(result.Seasons.First().Episodes);
    }
}
