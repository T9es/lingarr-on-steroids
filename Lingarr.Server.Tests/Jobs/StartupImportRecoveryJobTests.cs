using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Integration;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models.Integrations;
using Lingarr.Server.Models.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Jobs;

public class StartupImportRecoveryJobTests
{
    [Fact]
    public async Task Execute_WhenImportedEpisodeIsMissing_EnqueuesDownloadWebhook()
    {
        await using var dbContext = CreateDbContext();
        var sonarrService = new Mock<ISonarrService>();
        var instanceConfigService = new Mock<IInstanceConfigService>();
        var backgroundJobClient = CreateBackgroundJobClientMock(out var createdJobs);

        instanceConfigService
            .Setup(service => service.GetSonarrConfigs())
            .ReturnsAsync([new InstanceConfig("http://sonarr", "api-key", "default")]);
        sonarrService
            .Setup(service => service.GetImportHistory(1, StartupImportRecoveryJob.HistoryPageSize, "http://sonarr", "api-key"))
            .ReturnsAsync(CreateHistoryPage(1001, 42, "Test Show", DateTime.UtcNow.AddHours(-1)));

        var job = CreateJob(sonarrService, instanceConfigService, dbContext, backgroundJobClient);
        await job.Execute();

        var created = Assert.Single(createdJobs);
        Assert.Equal(typeof(WebhookJob), created.Type);
        Assert.Equal("ProcessSonarrWebhook", created.Method.Name);
        var payload = Assert.IsType<SonarrWebhookPayload>(created.Args[0]);
        Assert.Equal("Download", payload.EventType);
        Assert.NotNull(payload.Series);
        Assert.Equal(42, payload.Series!.Id);
        Assert.Equal("Test Show", payload.Series.Title);
        var episode = Assert.Single(payload.Episodes!);
        Assert.Equal(1001, episode.Id);
        Assert.Equal("default", created.Args[1]);
    }

    [Fact]
    public async Task Execute_WhenEpisodeRowAlreadyExists_DoesNotEnqueueWebhook()
    {
        await using var dbContext = CreateDbContext();
        var show = new Show
        {
            SonarrId = 42,
            Title = "Test Show",
            Path = "/media/tv/Test Show (2026)",
            DateAdded = DateTime.UtcNow,
            SourceInstanceId = "default"
        };
        var season = new Season { SeasonNumber = 1, Path = "/media/tv/Test Show (2026)/Season 01", Show = show };
        dbContext.Episodes.Add(new Episode
        {
            SonarrId = 1001,
            EpisodeNumber = 1,
            Title = "Test Episode",
            Season = season,
            SourceInstanceId = "default"
        });
        await dbContext.SaveChangesAsync();

        var sonarrService = new Mock<ISonarrService>();
        var instanceConfigService = new Mock<IInstanceConfigService>();
        var backgroundJobClient = CreateBackgroundJobClientMock(out var createdJobs);

        instanceConfigService
            .Setup(service => service.GetSonarrConfigs())
            .ReturnsAsync([new InstanceConfig("http://sonarr", "api-key", "default")]);
        sonarrService
            .Setup(service => service.GetImportHistory(1, StartupImportRecoveryJob.HistoryPageSize, "http://sonarr", "api-key"))
            .ReturnsAsync(CreateHistoryPage(1001, 42, "Test Show", DateTime.UtcNow.AddHours(-1)));

        var job = CreateJob(sonarrService, instanceConfigService, dbContext, backgroundJobClient);
        await job.Execute();

        Assert.Empty(createdJobs);
    }

    [Fact]
    public async Task Execute_WhenOnlyAnotherInstanceHasTheEpisode_RecoversIt()
    {
        await using var dbContext = CreateDbContext();
        var show = new Show
        {
            SonarrId = 42,
            Title = "Test Show",
            Path = "/media/tv/Test Show (2026)",
            DateAdded = DateTime.UtcNow,
            SourceInstanceId = "anime"
        };
        var season = new Season { SeasonNumber = 1, Path = "/media/tv/Test Show (2026)/Season 01", Show = show };
        dbContext.Episodes.Add(new Episode
        {
            SonarrId = 1001,
            EpisodeNumber = 1,
            Title = "Test Episode",
            Season = season,
            SourceInstanceId = "anime"
        });
        await dbContext.SaveChangesAsync();

        var sonarrService = new Mock<ISonarrService>();
        var instanceConfigService = new Mock<IInstanceConfigService>();
        var backgroundJobClient = CreateBackgroundJobClientMock(out var createdJobs);

        instanceConfigService
            .Setup(service => service.GetSonarrConfigs())
            .ReturnsAsync([new InstanceConfig("http://sonarr", "api-key", "default")]);
        sonarrService
            .Setup(service => service.GetImportHistory(1, StartupImportRecoveryJob.HistoryPageSize, "http://sonarr", "api-key"))
            .ReturnsAsync(CreateHistoryPage(1001, 42, "Test Show", DateTime.UtcNow.AddHours(-1)));

        var job = CreateJob(sonarrService, instanceConfigService, dbContext, backgroundJobClient);
        await job.Execute();

        var created = Assert.Single(createdJobs);
        var payload = Assert.IsType<SonarrWebhookPayload>(created.Args[0]);
        Assert.Equal(1001, Assert.Single(payload.Episodes!).Id);
    }

    [Fact]
    public async Task Execute_WhenImportIsOlderThanRecoveryWindow_IgnoresIt()
    {
        await using var dbContext = CreateDbContext();
        var sonarrService = new Mock<ISonarrService>();
        var instanceConfigService = new Mock<IInstanceConfigService>();
        var backgroundJobClient = CreateBackgroundJobClientMock(out var createdJobs);

        instanceConfigService
            .Setup(service => service.GetSonarrConfigs())
            .ReturnsAsync([new InstanceConfig("http://sonarr", "api-key", "default")]);
        sonarrService
            .Setup(service => service.GetImportHistory(1, StartupImportRecoveryJob.HistoryPageSize, "http://sonarr", "api-key"))
            .ReturnsAsync(CreateHistoryPage(
                1001,
                42,
                "Test Show",
                DateTime.UtcNow - StartupImportRecoveryJob.RecoveryWindow - TimeSpan.FromHours(1)));

        var job = CreateJob(sonarrService, instanceConfigService, dbContext, backgroundJobClient);
        await job.Execute();

        Assert.Empty(createdJobs);
    }

    [Fact]
    public async Task Execute_WhenNoInstancesConfigured_DoesNotQuerySonarr()
    {
        await using var dbContext = CreateDbContext();
        var sonarrService = new Mock<ISonarrService>(MockBehavior.Strict);
        var instanceConfigService = new Mock<IInstanceConfigService>();
        var backgroundJobClient = CreateBackgroundJobClientMock(out var createdJobs);

        instanceConfigService
            .Setup(service => service.GetSonarrConfigs())
            .ReturnsAsync([]);

        var job = CreateJob(sonarrService, instanceConfigService, dbContext, backgroundJobClient);
        await job.Execute();

        Assert.Empty(createdJobs);
    }

    [Fact]
    public async Task Execute_WhenHistoryCannotBeRead_DoesNotThrowOrEnqueue()
    {
        await using var dbContext = CreateDbContext();
        var sonarrService = new Mock<ISonarrService>();
        var instanceConfigService = new Mock<IInstanceConfigService>();
        var backgroundJobClient = CreateBackgroundJobClientMock(out var createdJobs);

        instanceConfigService
            .Setup(service => service.GetSonarrConfigs())
            .ReturnsAsync([new InstanceConfig("http://sonarr", "api-key", "default")]);
        sonarrService
            .Setup(service => service.GetImportHistory(1, StartupImportRecoveryJob.HistoryPageSize, "http://sonarr", "api-key"))
            .ReturnsAsync((SonarrHistoryPage?)null);

        var job = CreateJob(sonarrService, instanceConfigService, dbContext, backgroundJobClient);
        await job.Execute();

        Assert.Empty(createdJobs);
    }

    [Fact]
    public async Task Execute_WhenOneInstanceFails_ContinuesWithTheNextInstance()
    {
        await using var dbContext = CreateDbContext();
        var sonarrService = new Mock<ISonarrService>();
        var instanceConfigService = new Mock<IInstanceConfigService>();
        var backgroundJobClient = CreateBackgroundJobClientMock(out var createdJobs);

        instanceConfigService
            .Setup(service => service.GetSonarrConfigs())
            .ReturnsAsync(
            [
                new InstanceConfig("http://sonarr-broken", "api-key", "broken"),
                new InstanceConfig("http://sonarr", "api-key", "default")
            ]);
        sonarrService
            .Setup(service => service.GetImportHistory(1, StartupImportRecoveryJob.HistoryPageSize, "http://sonarr-broken", "api-key"))
            .ThrowsAsync(new HttpRequestException("connection refused"));
        sonarrService
            .Setup(service => service.GetImportHistory(1, StartupImportRecoveryJob.HistoryPageSize, "http://sonarr", "api-key"))
            .ReturnsAsync(CreateHistoryPage(1001, 42, "Test Show", DateTime.UtcNow.AddHours(-1)));

        var job = CreateJob(sonarrService, instanceConfigService, dbContext, backgroundJobClient);
        await job.Execute();

        var created = Assert.Single(createdJobs);
        Assert.Equal("default", created.Args[1]);
    }

    [Fact]
    public async Task Execute_WhenHistoryRepeatsAnEpisode_EnqueuesItOnce()
    {
        await using var dbContext = CreateDbContext();
        var sonarrService = new Mock<ISonarrService>();
        var instanceConfigService = new Mock<IInstanceConfigService>();
        var backgroundJobClient = CreateBackgroundJobClientMock(out var createdJobs);

        instanceConfigService
            .Setup(service => service.GetSonarrConfigs())
            .ReturnsAsync([new InstanceConfig("http://sonarr", "api-key", "default")]);

        var page = new SonarrHistoryPage
        {
            Records =
            [
                CreateRecord(1001, 42, "Test Show", DateTime.UtcNow.AddHours(-1)),
                CreateRecord(1001, 42, "Test Show", DateTime.UtcNow.AddHours(-2))
            ]
        };
        sonarrService
            .Setup(service => service.GetImportHistory(1, StartupImportRecoveryJob.HistoryPageSize, "http://sonarr", "api-key"))
            .ReturnsAsync(page);

        var job = CreateJob(sonarrService, instanceConfigService, dbContext, backgroundJobClient);
        await job.Execute();

        var created = Assert.Single(createdJobs);
        var payload = Assert.IsType<SonarrWebhookPayload>(created.Args[0]);
        Assert.Equal(1001, Assert.Single(payload.Episodes!).Id);
    }

    [Fact]
    public async Task Execute_WhenEpisodeRowIsMarkedMissing_RecoversIt()
    {
        await using var dbContext = CreateDbContext();
        var show = new Show
        {
            SonarrId = 42,
            Title = "Test Show",
            Path = "/media/tv/Test Show (2026)",
            DateAdded = DateTime.UtcNow,
            SourceInstanceId = "default"
        };
        var season = new Season { SeasonNumber = 1, Path = "/media/tv/Test Show (2026)/Season 01", Show = show };
        dbContext.Episodes.Add(new Episode
        {
            SonarrId = 1001,
            EpisodeNumber = 1,
            Title = "Test Episode",
            Season = season,
            SourceInstanceId = "default",
            MissingSince = DateTime.UtcNow.AddDays(-1)
        });
        await dbContext.SaveChangesAsync();

        var sonarrService = new Mock<ISonarrService>();
        var instanceConfigService = new Mock<IInstanceConfigService>();
        var backgroundJobClient = CreateBackgroundJobClientMock(out var createdJobs);

        instanceConfigService
            .Setup(service => service.GetSonarrConfigs())
            .ReturnsAsync([new InstanceConfig("http://sonarr", "api-key", "default")]);
        sonarrService
            .Setup(service => service.GetImportHistory(1, StartupImportRecoveryJob.HistoryPageSize, "http://sonarr", "api-key"))
            .ReturnsAsync(CreateHistoryPage(1001, 42, "Test Show", DateTime.UtcNow.AddHours(-1)));

        var job = CreateJob(sonarrService, instanceConfigService, dbContext, backgroundJobClient);
        await job.Execute();

        var created = Assert.Single(createdJobs);
        var payload = Assert.IsType<SonarrWebhookPayload>(created.Args[0]);
        Assert.Equal(1001, Assert.Single(payload.Episodes!).Id);
    }

    [Fact]
    public async Task Execute_GroupsRecoveredEpisodesBySeries()
    {
        await using var dbContext = CreateDbContext();
        var sonarrService = new Mock<ISonarrService>();
        var instanceConfigService = new Mock<IInstanceConfigService>();
        var backgroundJobClient = CreateBackgroundJobClientMock(out var createdJobs);

        instanceConfigService
            .Setup(service => service.GetSonarrConfigs())
            .ReturnsAsync([new InstanceConfig("http://sonarr", "api-key", "default")]);
        sonarrService
            .Setup(service => service.GetImportHistory(1, StartupImportRecoveryJob.HistoryPageSize, "http://sonarr", "api-key"))
            .ReturnsAsync(CreateHistoryPage(
                CreateRecord(1001, 42, "Test Show", DateTime.UtcNow.AddHours(-1)),
                CreateRecord(1002, 42, "Test Show", DateTime.UtcNow.AddHours(-2)),
                CreateRecord(2001, 7, "Another Show", DateTime.UtcNow.AddHours(-3))));

        var job = CreateJob(sonarrService, instanceConfigService, dbContext, backgroundJobClient);
        await job.Execute();

        Assert.Equal(2, createdJobs.Count);
        var firstSeriesPayload = Assert.IsType<SonarrWebhookPayload>(
            Assert.Single(createdJobs, job => ((SonarrWebhookPayload)job.Args[0]).Series!.Id == 42).Args[0]);
        Assert.Equal(2, firstSeriesPayload.Episodes!.Count);
        var otherPayload = Assert.IsType<SonarrWebhookPayload>(
            Assert.Single(createdJobs, job => ((SonarrWebhookPayload)job.Args[0]).Series!.Id == 7).Args[0]);
        Assert.Equal(2001, Assert.Single(otherPayload.Episodes!).Id);
    }

    [Fact]
    public async Task Execute_WhenHistoryHasInvalidEpisodeId_IgnoresIt()
    {
        await using var dbContext = CreateDbContext();
        var sonarrService = new Mock<ISonarrService>();
        var instanceConfigService = new Mock<IInstanceConfigService>();
        var backgroundJobClient = CreateBackgroundJobClientMock(out var createdJobs);

        instanceConfigService
            .Setup(service => service.GetSonarrConfigs())
            .ReturnsAsync([new InstanceConfig("http://sonarr", "api-key", "default")]);
        sonarrService
            .Setup(service => service.GetImportHistory(1, StartupImportRecoveryJob.HistoryPageSize, "http://sonarr", "api-key"))
            .ReturnsAsync(CreateHistoryPage(
                CreateRecord(0, 42, "Test Show", DateTime.UtcNow.AddHours(-1)),
                CreateRecord(1001, 42, "Test Show", DateTime.UtcNow.AddHours(-2))));

        var job = CreateJob(sonarrService, instanceConfigService, dbContext, backgroundJobClient);
        await job.Execute();

        var created = Assert.Single(createdJobs);
        var payload = Assert.IsType<SonarrWebhookPayload>(created.Args[0]);
        Assert.Equal(1001, Assert.Single(payload.Episodes!).Id);
    }

    [Fact]
    public async Task Execute_WhenWindowIsNotReachedWithinOnePage_ReadsNextPageAndStopsAtWindowEdge()
    {
        await using var dbContext = CreateDbContext();
        var sonarrService = new Mock<ISonarrService>();
        var instanceConfigService = new Mock<IInstanceConfigService>();
        var backgroundJobClient = CreateBackgroundJobClientMock(out var createdJobs);

        instanceConfigService
            .Setup(service => service.GetSonarrConfigs())
            .ReturnsAsync([new InstanceConfig("http://sonarr", "api-key", "default")]);

        var firstPageRecords = Enumerable
            .Range(0, StartupImportRecoveryJob.HistoryPageSize)
            .Select(index => CreateRecord(10000 + index, 42, "Test Show", DateTime.UtcNow.AddMinutes(-index - 1)))
            .ToArray();
        sonarrService
            .Setup(service => service.GetImportHistory(1, StartupImportRecoveryJob.HistoryPageSize, "http://sonarr", "api-key"))
            .ReturnsAsync(CreateHistoryPage(firstPageRecords));
        sonarrService
            .Setup(service => service.GetImportHistory(2, StartupImportRecoveryJob.HistoryPageSize, "http://sonarr", "api-key"))
            .ReturnsAsync(CreateHistoryPage(
                CreateRecord(1001, 42, "Test Show", DateTime.UtcNow.AddHours(-2)),
                CreateRecord(1002, 42, "Test Show", DateTime.UtcNow.AddDays(-3))));

        var job = CreateJob(sonarrService, instanceConfigService, dbContext, backgroundJobClient);
        await job.Execute();

        sonarrService.Verify(
            service => service.GetImportHistory(1, StartupImportRecoveryJob.HistoryPageSize, "http://sonarr", "api-key"),
            Times.Once);
        sonarrService.Verify(
            service => service.GetImportHistory(2, StartupImportRecoveryJob.HistoryPageSize, "http://sonarr", "api-key"),
            Times.Once);
        sonarrService.Verify(
            service => service.GetImportHistory(3, StartupImportRecoveryJob.HistoryPageSize, "http://sonarr", "api-key"),
            Times.Never);
        var recoveredIds = createdJobs
            .SelectMany(job => ((SonarrWebhookPayload)job.Args[0]).Episodes!)
            .Select(episode => episode.Id)
            .ToHashSet();
        Assert.Equal(StartupImportRecoveryJob.HistoryPageSize + 1, recoveredIds.Count);
        Assert.Contains(1001, recoveredIds);
        Assert.DoesNotContain(1002, recoveredIds);
    }

    [Fact]
    public async Task Execute_WhenMoreEpisodesAreMissingThanTheCap_RecoversAtMostTheCap()
    {
        await using var dbContext = CreateDbContext();
        var sonarrService = new Mock<ISonarrService>();
        var instanceConfigService = new Mock<IInstanceConfigService>();
        var backgroundJobClient = CreateBackgroundJobClientMock(out var createdJobs);

        instanceConfigService
            .Setup(service => service.GetSonarrConfigs())
            .ReturnsAsync([new InstanceConfig("http://sonarr", "api-key", "default")]);
        for (var page = 1; page <= 3; page++)
        {
            var records = Enumerable
                .Range(0, StartupImportRecoveryJob.HistoryPageSize)
                .Select(index => CreateRecord(
                    page * 1000 + index,
                    42,
                    "Test Show",
                    DateTime.UtcNow.AddMinutes(-index - 1)))
                .ToArray();
            sonarrService
                .Setup(service => service.GetImportHistory(page, StartupImportRecoveryJob.HistoryPageSize, "http://sonarr", "api-key"))
                .ReturnsAsync(CreateHistoryPage(records));
        }

        var job = CreateJob(sonarrService, instanceConfigService, dbContext, backgroundJobClient);
        await job.Execute();

        var recoveredEpisodes = createdJobs
            .SelectMany(job => ((SonarrWebhookPayload)job.Args[0]).Episodes!)
            .ToList();
        Assert.Equal(StartupImportRecoveryJob.MaxRecoveredEpisodes, recoveredEpisodes.Count);
    }

    private static StartupImportRecoveryJob CreateJob(
        Mock<ISonarrService> sonarrService,
        Mock<IInstanceConfigService> instanceConfigService,
        LingarrDbContext dbContext,
        IBackgroundJobClient backgroundJobClient)
    {
        return new StartupImportRecoveryJob(
            sonarrService.Object,
            instanceConfigService.Object,
            dbContext,
            backgroundJobClient,
            NullLogger<StartupImportRecoveryJob>.Instance);
    }

    private static LingarrDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LingarrDbContext(options);
    }

    private static IBackgroundJobClient CreateBackgroundJobClientMock(out List<Job> createdJobs)
    {
        var jobs = new List<Job>();
        var mock = new Mock<IBackgroundJobClient>();
        mock
            .Setup(client => client.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Callback<Job, IState>((job, _) => jobs.Add(job))
            .Returns(() => Guid.NewGuid().ToString());
        createdJobs = jobs;
        return mock.Object;
    }

    private static SonarrHistoryPage CreateHistoryPage(
        int episodeId,
        int seriesId,
        string seriesTitle,
        DateTime date)
    {
        return new SonarrHistoryPage
        {
            Records = [CreateRecord(episodeId, seriesId, seriesTitle, date)]
        };
    }

    private static SonarrHistoryPage CreateHistoryPage(params SonarrHistoryRecord[] records)
    {
        return new SonarrHistoryPage { Records = records.ToList() };
    }

    private static SonarrHistoryRecord CreateRecord(
        int episodeId,
        int seriesId,
        string seriesTitle,
        DateTime date)
    {
        return new SonarrHistoryRecord
        {
            EpisodeId = episodeId,
            SeriesId = seriesId,
            Date = date,
            Series = new SonarrHistorySeries
            {
                Title = seriesTitle,
                Path = "/media/tv/Test Show (2026)",
                TvdbId = 12345
            }
        };
    }
}
