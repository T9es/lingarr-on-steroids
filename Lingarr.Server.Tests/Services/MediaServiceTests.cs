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
        var instanceConfigServiceMock = new Mock<IInstanceConfigService>();
        var logger = NullLogger<MediaService>.Instance;

        // Configure instance config service to return fallback credentials
        instanceConfigServiceMock
            .Setup(s => s.GetSonarrConfig(It.IsAny<string?>()))
            .ReturnsAsync(new InstanceConfig("http://test.sonarr.com", "test-api-key", "default"));

        // Configure Sonarr to throw HttpRequestException with 404 status for instance-aware overload
        sonarrMock
            .Setup(s => s.GetEpisode(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("Not found", null, HttpStatusCode.NotFound));

        // Configure GetShows to return a sample list when called
        sonarrMock
            .Setup(s => s.GetShows(It.IsAny<string>(), It.IsAny<string>()))
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
            instanceConfigServiceMock.Object,
            logger);

        // Act
        var result = await mediaService.GetEpisodeIdOrSyncFromSonarrEpisodeId(150);

        // Assert
        sonarrMock.Verify(s => s.GetEpisode(150, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        sonarrMock.Verify(s => s.GetShows(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
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
        var instanceConfigServiceMock = new Mock<IInstanceConfigService>();
        var logger = NullLogger<MediaService>.Instance;

        instanceConfigServiceMock
            .Setup(s => s.GetSonarrConfig(It.IsAny<string?>()))
            .ReturnsAsync(new InstanceConfig("http://test.sonarr.com", "test-api-key", "default"));

        var mediaService = new MediaService(context,
            subtitleMock.Object,
            sonarrMock.Object,
            showSyncServiceMock.Object,
            radarrMock.Object,
            movieSyncMock.Object,
            mediaSubtitleProcessorMock.Object,
            instanceConfigServiceMock.Object,
            logger);

        // Act
        var result = await mediaService.GetShow(2);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Detailed Show", result!.Title);
        Assert.Single(result.Seasons);
        Assert.Single(result.Seasons.First().Episodes);
    }

    [Fact]
    public async Task GetEpisodeIdOrSyncFromSonarrEpisodeId_UsesCorrectInstanceId_ForFallbackSync()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new LingarrDbContext(options);
        await context.SaveChangesAsync();

        var sonarrMock = new Mock<ISonarrService>();
        var radarrMock = new Mock<IRadarrService>();
        var movieSyncMock = new Mock<IMovieSyncService>();
        var subtitleMock = new Mock<ISubtitleService>();
        var showSyncServiceMock = new Mock<IShowSyncService>();
        var mediaSubtitleProcessorMock = new Mock<IMediaSubtitleProcessor>();
        var instanceConfigServiceMock = new Mock<IInstanceConfigService>();
        var logger = NullLogger<MediaService>.Instance;

        const string testInstanceId = "my-sonarr-instance";

        instanceConfigServiceMock
            .Setup(s => s.GetSonarrConfig(It.IsAny<string?>()))
            .ReturnsAsync(new InstanceConfig("http://test.sonarr.com", "test-api-key", "default"));

        sonarrMock
            .Setup(s => s.GetEpisode(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("Not found", null, HttpStatusCode.NotFound));

        sonarrMock
            .Setup(s => s.GetShows(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<Lingarr.Server.Models.Integrations.SonarrShow>
            {
                new Lingarr.Server.Models.Integrations.SonarrShow
                {
                    Id = 1,
                    Title = "Sample Show",
                    Path = "/tmp",
                    Added = DateTime.UtcNow.ToString("o"),
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
            instanceConfigServiceMock.Object,
            logger);

        // Act
        var result = await mediaService.GetEpisodeIdOrSyncFromSonarrEpisodeId(150, testInstanceId);

        // Assert - verify the fallback sync uses the correct instanceId, not hardcoded "default"
        showSyncServiceMock.Verify(s => s.SyncShows(
            It.Is<List<(Lingarr.Server.Models.Integrations.SonarrShow Show, string InstanceId)>>(
                list => list.All(item => item.InstanceId == testInstanceId))),
            Times.Once);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task GetMovieIdOrSyncFromRadarrMovieId_UsesCorrectInstanceId()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new LingarrDbContext(options);
        await context.SaveChangesAsync();

        const string testInstanceId = "my-radarr-instance";
        const int radarrMovieId = 42;

        var sonarrMock = new Mock<ISonarrService>();
        var radarrMock = new Mock<IRadarrService>();
        var movieSyncMock = new Mock<IMovieSyncService>();
        var subtitleMock = new Mock<ISubtitleService>();
        var showSyncServiceMock = new Mock<IShowSyncService>();
        var mediaSubtitleProcessorMock = new Mock<IMediaSubtitleProcessor>();
        var instanceConfigServiceMock = new Mock<IInstanceConfigService>();
        var logger = NullLogger<MediaService>.Instance;

        instanceConfigServiceMock
            .Setup(s => s.GetRadarrConfig(It.IsAny<string?>()))
            .ReturnsAsync(new InstanceConfig("http://test.radarr.com", "test-api-key", testInstanceId));

        radarrMock
            .Setup(s => s.GetMovie(radarrMovieId, "http://test.radarr.com", "test-api-key"))
            .ReturnsAsync(new Lingarr.Server.Models.Integrations.RadarrMovie
            {
                Id = radarrMovieId,
                Title = "Test Movie",
                Path = "/movies/test/movie.mkv",
                RootFolderPath = "/movies",
                Added = DateTime.UtcNow.ToString("o"),
                HasFile = true,
                MovieFile = new Lingarr.Server.Models.Integrations.RadarrMovieFile()
            });

        movieSyncMock
            .Setup(s => s.SyncMovie(It.IsAny<Lingarr.Server.Models.Integrations.RadarrMovie>(), testInstanceId))
            .ReturnsAsync(new Lingarr.Core.Entities.Movie
            {
                Id = 100,
                RadarrId = radarrMovieId,
                Title = "Test Movie",
                Path = "/movies/test",
                FileName = "test.mkv",
                DateAdded = DateTime.UtcNow,
                SourceInstanceId = testInstanceId
            });

        var mediaService = new MediaService(context,
            subtitleMock.Object,
            sonarrMock.Object,
            showSyncServiceMock.Object,
            radarrMock.Object,
            movieSyncMock.Object,
            mediaSubtitleProcessorMock.Object,
            instanceConfigServiceMock.Object,
            logger);

        // Act
        var result = await mediaService.GetMovieIdOrSyncFromRadarrMovieId(radarrMovieId, testInstanceId);

        // Assert
        Assert.Equal(100, result);
        movieSyncMock.Verify(s => s.SyncMovie(
            It.IsAny<Lingarr.Server.Models.Integrations.RadarrMovie>(),
            testInstanceId),
            Times.Once);
    }
}
