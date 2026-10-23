using System;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Jobs;
using Lingarr.Server.Services.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Jobs;

public class PruneMissingMediaJobTests
{
    [Fact]
    public async Task ExecuteInternalAsync_WithZeroWindow_DoesNotDeleteAnything()
    {
        await using var context = BuildContext();
        await SeedMissingMovieAsync(context, DateTime.UtcNow.AddDays(-30));
        var job = CreateJob(context, settingValue: "0");

        var result = await job.ExecuteInternalAsync(dryRun: false);

        Assert.Equal(0, result.WindowDays);
        Assert.Equal(0, result.MoviesPruned);
        Assert.Equal(1, await context.Movies.CountAsync());
    }

    [Fact]
    public async Task ExecuteInternalAsync_WithMissingSetting_UsesDefaultWindowAndPrunesOldRows()
    {
        await using var context = BuildContext();
        await SeedMissingMovieAsync(context, DateTime.UtcNow.AddDays(-8));
        await SeedMissingEpisodeAsync(context, DateTime.UtcNow.AddDays(-8));
        var job = CreateJob(context, settingValue: null);

        var result = await job.ExecuteInternalAsync(dryRun: false);

        Assert.Equal(MissingMediaPrunePolicy.DefaultPruneWindowDays, result.WindowDays);
        Assert.Equal(1, result.MoviesPruned);
        Assert.Equal(1, result.EpisodesPruned);
        Assert.Equal(0, await context.Movies.CountAsync());
        Assert.Equal(0, await context.Episodes.CountAsync());
    }

    [Fact]
    public async Task ExecuteInternalAsync_KeepsRowsInsideGraceWindow()
    {
        await using var context = BuildContext();
        await SeedMissingMovieAsync(context, DateTime.UtcNow.AddDays(-2));
        var job = CreateJob(context, settingValue: "7");

        var result = await job.ExecuteInternalAsync(dryRun: false);

        Assert.Equal(0, result.MoviesPruned);
        Assert.Equal(1, await context.Movies.CountAsync());
    }

    [Fact]
    public async Task ExecuteInternalAsync_KeepsRowsWithHistoryVisibleAndClearsMarker()
    {
        await using var context = BuildContext();
        var movie = await SeedMissingMovieAsync(context, DateTime.UtcNow.AddDays(-30));
        context.TranslationRequests.Add(new TranslationRequest
        {
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            WorkloadKind = TranslationWorkloadKind.Library,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            Status = TranslationStatus.Completed
        });
        await context.SaveChangesAsync();
        var job = CreateJob(context, settingValue: "7");

        var result = await job.ExecuteInternalAsync(dryRun: false);

        Assert.Equal(0, result.MoviesPruned);
        Assert.Equal(1, result.RowsKeptVisible);
        Assert.Equal(1, await context.Movies.CountAsync());
        Assert.Null(movie.MissingSince);
    }

    [Fact]
    public async Task ExecuteInternalAsync_DryRun_ReportsCandidatesWithoutDeleting()
    {
        await using var context = BuildContext();
        var movie = await SeedMissingMovieAsync(context, DateTime.UtcNow.AddDays(-8));
        var job = CreateJob(context, settingValue: "7");

        var result = await job.ExecuteInternalAsync(dryRun: true);

        Assert.True(result.DryRun);
        Assert.Equal(1, result.MoviesPruned);
        Assert.Equal(1, await context.Movies.CountAsync());
        Assert.NotNull(movie.MissingSince);
    }

    [Fact]
    public async Task ExecuteInternalAsync_RunTwice_IsIdempotent()
    {
        await using var context = BuildContext();
        await SeedMissingMovieAsync(context, DateTime.UtcNow.AddDays(-8));
        var job = CreateJob(context, settingValue: "7");

        var first = await job.ExecuteInternalAsync(dryRun: false);
        var second = await job.ExecuteInternalAsync(dryRun: false);

        Assert.Equal(1, first.MoviesPruned);
        Assert.Equal(0, second.MoviesPruned);
        Assert.Equal(0, await context.Movies.CountAsync());
    }

    [Fact]
    public async Task ExecuteInternalAsync_WithoutLimit_StopsAtBoundedBatchSize()
    {
        await using var context = BuildContext();
        await SeedMissingMovieAsync(context, DateTime.UtcNow.AddDays(-10));
        await SeedMissingMovieAsync(context, DateTime.UtcNow.AddDays(-9));
        await SeedMissingMovieAsync(context, DateTime.UtcNow.AddDays(-8));
        var job = CreateJob(context, settingValue: "7");

        var result = await job.ExecuteInternalAsync(dryRun: false, maxItems: 2);

        Assert.Equal(2, result.MoviesPruned);
        Assert.Equal(1, await context.Movies.CountAsync());
    }

    private static PruneMissingMediaJob CreateJob(LingarrDbContext context, string? settingValue)
    {
        var settingServiceMock = new Mock<ISettingService>();
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Maintenance.PruneMissingMediaDays))
            .ReturnsAsync(settingValue);
        var scheduleServiceMock = new Mock<IScheduleService>();

        return new PruneMissingMediaJob(
            context,
            settingServiceMock.Object,
            scheduleServiceMock.Object,
            NullLogger<PruneMissingMediaJob>.Instance);
    }

    private static async Task<Movie> SeedMissingMovieAsync(LingarrDbContext context, DateTime missingSince)
    {
        var movie = new Movie
        {
            RadarrId = Random.Shared.Next(1, 1_000_000),
            Title = "Missing Movie",
            FileName = "missing-movie",
            Path = "/movies/missing-movie",
            DateAdded = DateTime.UtcNow.AddDays(-30),
            SourceInstanceId = "default",
            TranslationState = TranslationState.AwaitingSource,
            MissingSince = missingSince
        };
        context.Movies.Add(movie);
        await context.SaveChangesAsync();
        return movie;
    }

    private static async Task<Episode> SeedMissingEpisodeAsync(LingarrDbContext context, DateTime missingSince)
    {
        var show = new Show
        {
            SonarrId = Random.Shared.Next(1, 1_000_000),
            Title = "Show",
            Path = "/shows/show",
            DateAdded = DateTime.UtcNow.AddDays(-30),
            SourceInstanceId = "default"
        };
        var season = new Season
        {
            SeasonNumber = 1,
            Show = show
        };
        var episode = new Episode
        {
            SonarrId = Random.Shared.Next(1, 1_000_000),
            EpisodeNumber = 1,
            Title = "Missing Episode",
            FileName = "missing-episode",
            Path = "/shows/show",
            DateAdded = DateTime.UtcNow.AddDays(-30),
            Season = season,
            SourceInstanceId = "default",
            TranslationState = TranslationState.AwaitingSource,
            MissingSince = missingSince
        };
        season.Episodes.Add(episode);
        show.Seasons.Add(season);
        context.Shows.Add(show);
        await context.SaveChangesAsync();
        return episode;
    }

    private static LingarrDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LingarrDbContext(options);
    }
}
