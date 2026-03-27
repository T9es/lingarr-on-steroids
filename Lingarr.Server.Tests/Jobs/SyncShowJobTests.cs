using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Integration;
using Lingarr.Server.Interfaces.Services.Sync;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Integrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Jobs;

public class SyncShowJobTests
{
    [Fact]
    public async Task Execute_RemovesOrphanedShowSeasonAndEpisodeTranslationRequests()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateSqliteContext(connection);

        var activeShow = new Show
        {
            SonarrId = 1,
            Title = "Active Show",
            Path = "/shows/active",
            DateAdded = DateTime.UtcNow,
            SourceInstanceId = "default"
        };
        var activeSeason = new Season
        {
            SeasonNumber = 1,
            Path = "/shows/active/season1",
            Show = activeShow
        };
        activeShow.Seasons.Add(activeSeason);
        var activeEpisode = new Episode
        {
            SonarrId = 11,
            SourceInstanceId = "default",
            EpisodeNumber = 1,
            Title = "Episode 1",
            FileName = "episode1",
            Path = "/shows/active/season1",
            Season = activeSeason
        };
        activeSeason.Episodes.Add(activeEpisode);

        var orphanedShow = new Show
        {
            SonarrId = 2,
            Title = "Orphaned Show",
            Path = "/shows/orphaned",
            DateAdded = DateTime.UtcNow,
            SourceInstanceId = "deleted-instance"
        };
        var orphanedSeason = new Season
        {
            SeasonNumber = 1,
            Path = "/shows/orphaned/season1",
            Show = orphanedShow
        };
        orphanedShow.Seasons.Add(orphanedSeason);
        var orphanedEpisode = new Episode
        {
            SonarrId = 22,
            SourceInstanceId = "deleted-instance",
            EpisodeNumber = 1,
            Title = "Orphaned Episode",
            FileName = "orphaned",
            Path = "/shows/orphaned/season1",
            Season = orphanedSeason
        };
        orphanedSeason.Episodes.Add(orphanedEpisode);

        context.Shows.AddRange(activeShow, orphanedShow);
        await context.SaveChangesAsync();

        context.TranslationRequests.AddRange(
            new TranslationRequest
            {
                Title = "Active Show Request",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = MediaType.Show,
                MediaId = activeShow.Id,
                Status = TranslationStatus.Pending
            },
            new TranslationRequest
            {
                Title = "Orphan Show Request",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = MediaType.Show,
                MediaId = orphanedShow.Id,
                Status = TranslationStatus.Pending
            },
            new TranslationRequest
            {
                Title = "Orphan Season Request",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = MediaType.Season,
                MediaId = orphanedSeason.Id,
                Status = TranslationStatus.InProgress
            },
            new TranslationRequest
            {
                Title = "Orphan Episode Request",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = MediaType.Episode,
                MediaId = orphanedEpisode.Id,
                Status = TranslationStatus.Failed
            });
        await context.SaveChangesAsync();

        var sonarrServiceMock = new Mock<ISonarrService>();
        sonarrServiceMock
            .Setup(s => s.GetShows("http://sonarr", "api-key"))
            .ReturnsAsync(new List<SonarrShow>
            {
                new()
                {
                    Id = activeShow.SonarrId,
                    Title = activeShow.Title,
                    Path = activeShow.Path,
                    Added = DateTime.UtcNow.ToString("O"),
                    SeasonFolder = true,
                    Seasons = new List<SonarrSeason>()
                }
            });

        var scheduleServiceMock = new Mock<IScheduleService>();
        scheduleServiceMock
            .Setup(s => s.UpdateJobState(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var showSyncServiceMock = new Mock<IShowSyncService>();
        showSyncServiceMock
            .Setup(s => s.SyncShows(It.IsAny<List<(SonarrShow Show, string InstanceId)>>()))
            .Returns(Task.CompletedTask);
        showSyncServiceMock
            .Setup(s => s.RemoveNonExistentShows(It.IsAny<IEnumerable<int>>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var settingServiceMock = new Mock<ISettingService>();
        settingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Integration.SonarrInstances))
            .ReturnsAsync(JsonSerializer.Serialize(new[]
            {
                new SonarrInstance { Id = "default", Name = "Default", Url = "http://sonarr", ApiKey = "api-key" }
            }));
        settingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Integration.SonarrUrl))
            .ReturnsAsync((string?)null);
        settingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Integration.SonarrApiKey))
            .ReturnsAsync((string?)null);

        var job = new SyncShowJob(
            context,
            sonarrServiceMock.Object,
            NullLogger<SyncShowJob>.Instance,
            scheduleServiceMock.Object,
            showSyncServiceMock.Object,
            settingServiceMock.Object);

        await job.Execute();

        var remainingRequests = await context.TranslationRequests.ToListAsync();
        Assert.Single(remainingRequests);
        Assert.Equal(activeShow.Id, remainingRequests[0].MediaId);
        Assert.Equal(MediaType.Show, remainingRequests[0].MediaType);
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
}
