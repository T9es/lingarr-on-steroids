using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Lingarr.Server.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class CleanupServiceTests
{
    [Fact]
    public async Task CleanupDuplicateInstances_ReassignsEpisodesAndPreservesDuplicateCounts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateSqliteContext(connection);

        var defaultShow = new Show
        {
            SonarrId = 100,
            Title = "Show",
            Path = "/library/default/show",
            DateAdded = DateTime.UtcNow,
            SourceInstanceId = "default"
        };
        var defaultSeason = new Season
        {
            SeasonNumber = 1,
            Path = "/library/default/show/season1",
            Show = defaultShow
        };
        defaultShow.Seasons.Add(defaultSeason);
        defaultSeason.Episodes.Add(new Episode
        {
            SonarrId = 500,
            SourceInstanceId = "default",
            EpisodeNumber = 1,
            Title = "Pilot",
            FileName = "pilot-default",
            Path = "/library/default/show/season1",
            Season = defaultSeason
        });

        var collidingShow = new Show
        {
            SonarrId = 100,
            Title = "Show",
            Path = "/library/other/show",
            DateAdded = DateTime.UtcNow,
            SourceInstanceId = "other"
        };
        var collidingSeason = new Season
        {
            SeasonNumber = 1,
            Path = "/library/other/show/season1",
            Show = collidingShow
        };
        collidingShow.Seasons.Add(collidingSeason);
        collidingSeason.Episodes.Add(new Episode
        {
            SonarrId = 500,
            SourceInstanceId = "other",
            EpisodeNumber = 1,
            Title = "Pilot",
            FileName = "pilot-other",
            Path = "/library/other/show/season1",
            Season = collidingSeason
        });

        var uniqueShow = new Show
        {
            SonarrId = 200,
            Title = "Unique Show",
            Path = "/library/other/unique",
            DateAdded = DateTime.UtcNow,
            SourceInstanceId = "other"
        };
        var uniqueSeason = new Season
        {
            SeasonNumber = 1,
            Path = "/library/other/unique/season1",
            Show = uniqueShow
        };
        uniqueShow.Seasons.Add(uniqueSeason);
        uniqueSeason.Episodes.Add(new Episode
        {
            SonarrId = 600,
            SourceInstanceId = "other",
            EpisodeNumber = 1,
            Title = "First",
            FileName = "first",
            Path = "/library/other/unique/season1",
            Season = uniqueSeason
        });

        context.Shows.AddRange(defaultShow, collidingShow, uniqueShow);
        await context.SaveChangesAsync();

        var settingValues = new Dictionary<string, string?>
        {
            [SettingKeys.Integration.SonarrInstances] = JsonSerializer.Serialize(new[]
            {
                new InstanceSetting { Id = "default", Name = "Default", Url = "http://sonarr-default", ApiKey = "key-1" },
                new InstanceSetting { Id = "other", Name = "Other", Url = "http://sonarr-other", ApiKey = "key-2" }
            })
        };

        var cleanupService = new CleanupService(
            context,
            CreateSettingServiceMock(settingValues).Object,
            NullLogger<CleanupService>.Instance);

        var result = await cleanupService.CleanupDuplicateInstances();

        Assert.True(result.Success);
        Assert.Equal(1, result.ShowsReassigned);
        Assert.Equal(1, result.EpisodesReassigned);
        Assert.Equal(1, result.DuplicatesRemoved);
        Assert.Equal(1, result.EpisodeDuplicatesRemoved);

        context.ChangeTracker.Clear();

        var remainingShows = await context.Shows
            .Include(s => s.Seasons)
            .ThenInclude(season => season.Episodes)
            .OrderBy(s => s.SonarrId)
            .ToListAsync();

        Assert.Equal(2, remainingShows.Count);
        Assert.All(remainingShows, show => Assert.Equal("default", show.SourceInstanceId));
        Assert.All(
            remainingShows.SelectMany(show => show.Seasons).SelectMany(season => season.Episodes),
            episode => Assert.Equal("default", episode.SourceInstanceId));

        var storedInstances = settingValues[SettingKeys.Integration.SonarrInstances];
        Assert.NotNull(storedInstances);
        var parsedInstances = JsonSerializer.Deserialize<List<InstanceSetting>>(storedInstances!);
        var remainingInstance = Assert.Single(parsedInstances!);
        Assert.Equal("default", remainingInstance.Id);
    }

    [Fact]
    public async Task GetDuplicateCleanupPreview_FlagsDuplicateBackendsAndNonDefaultMedia()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateSqliteContext(connection);

        context.Movies.Add(new Movie
        {
            RadarrId = 10,
            Title = "Movie",
            FileName = "movie",
            Path = "/movies/movie",
            DateAdded = DateTime.UtcNow,
            SourceInstanceId = "other"
        });

        var show = new Show
        {
            SonarrId = 20,
            Title = "Show",
            Path = "/shows/show",
            DateAdded = DateTime.UtcNow,
            SourceInstanceId = "other"
        };
        var season = new Season
        {
            SeasonNumber = 1,
            Path = "/shows/show/season1",
            Show = show
        };
        show.Seasons.Add(season);
        season.Episodes.Add(new Episode
        {
            SonarrId = 30,
            SourceInstanceId = "other",
            EpisodeNumber = 1,
            Title = "Episode",
            FileName = "episode",
            Path = "/shows/show/season1",
            Season = season
        });

        context.Shows.Add(show);
        await context.SaveChangesAsync();

        var settingValues = new Dictionary<string, string?>
        {
            [SettingKeys.Integration.RadarrInstances] = JsonSerializer.Serialize(new[]
            {
                new InstanceSetting { Id = "default", Name = "Radarr", Url = "http://radarr.local/", ApiKey = "one" },
                new InstanceSetting { Id = "copy", Name = "Radarr Copy", Url = "http://RADARR.local", ApiKey = "two" }
            }),
            [SettingKeys.Integration.SonarrInstances] = JsonSerializer.Serialize(new[]
            {
                new InstanceSetting { Id = "default", Name = "Sonarr", Url = "http://sonarr.local", ApiKey = "one" }
            })
        };

        var cleanupService = new CleanupService(
            context,
            CreateSettingServiceMock(settingValues).Object,
            NullLogger<CleanupService>.Instance);

        var preview = await cleanupService.GetDuplicateCleanupPreview();

        Assert.True(preview.HasCleanupCandidates);
        Assert.True(preview.HasDuplicateBackendConfigurations);
        Assert.Equal(1, preview.DuplicateBackendConfigurations);
        Assert.Equal(1, preview.NonDefaultMovieCount);
        Assert.Equal(1, preview.NonDefaultShowCount);
        Assert.Equal(1, preview.NonDefaultEpisodeCount);
    }

    private static LingarrDbContext CreateSqliteContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new LingarrDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private static Mock<ISettingService> CreateSettingServiceMock(Dictionary<string, string?> values)
    {
        var settingServiceMock = new Mock<ISettingService>();
        settingServiceMock
            .Setup(s => s.GetSetting(It.IsAny<string>()))
            .ReturnsAsync((string key) => values.TryGetValue(key, out var value) ? value : null);
        settingServiceMock
            .Setup(s => s.SetSetting(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string key, string value) =>
            {
                values[key] = value;
                return true;
            });

        return settingServiceMock;
    }
}
