using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class StartupServiceTests
{
    [Fact]
    public async Task StartAsync_SeedsSkipWhenTargetEmbeddedDefault()
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<LingarrDbContext>(builder => builder.UseInMemoryDatabase(databaseName));
        using var provider = services.BuildServiceProvider();

        var service = new StartupService(provider, NullLogger<StartupService>.Instance);

        await service.StartAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();

        var skipWhenTargetEmbedded = await context.Settings
            .SingleAsync(setting => setting.Key == SettingKeys.SubtitleValidation.SkipWhenTargetEmbedded);

        Assert.Equal("true", skipWhenTargetEmbedded.Value);
    }

    [Fact]
    public async Task StartAsync_SeedsPruneMissingMediaDaysDefault()
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<LingarrDbContext>(builder => builder.UseInMemoryDatabase(databaseName));
        using var provider = services.BuildServiceProvider();

        var service = new StartupService(provider, NullLogger<StartupService>.Instance);

        await service.StartAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();

        var pruneWindow = await context.Settings
            .SingleAsync(setting => setting.Key == SettingKeys.Maintenance.PruneMissingMediaDays);

        Assert.Equal("7", pruneWindow.Value);
    }

    [Fact]
    public async Task StartAsync_SeedsNanoGptProviderDefaults()
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<LingarrDbContext>(builder => builder.UseInMemoryDatabase(databaseName));
        using var provider = services.BuildServiceProvider();

        var service = new StartupService(provider, NullLogger<StartupService>.Instance);

        await service.StartAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();

        var settings = await context.Settings
            .Where(setting => setting.Key.StartsWith("nanogpt_"))
            .ToDictionaryAsync(setting => setting.Key, setting => setting.Value);

        Assert.Equal("true", settings[SettingKeys.Translation.NanoGpt.SubscriptionModelsOnly]);
        Assert.Equal("60000000", settings[SettingKeys.Translation.NanoGpt.WeeklyTokenAllowance]);
        Assert.Equal("0", settings[SettingKeys.Translation.NanoGpt.TokenReserve]);
        Assert.Equal("0", settings[SettingKeys.Translation.NanoGpt.DailyUnitReserve]);
        Assert.Equal("0", settings[SettingKeys.Translation.NanoGpt.MonthlyUnitReserve]);
    }

    [Fact]
    public async Task StartAsync_ReEvaluatesIndexedAwaitingSourceRowsOnce()
    {
        using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var options = CreateSqliteOptions(connection);

        int movieId;
        int episodeId;
        await using (var seedContext = new LingarrDbContext(options))
        {
            await seedContext.Database.EnsureCreatedAsync();
            var movie = CreateMovie(101, "awaiting.mkv", TranslationState.AwaitingSource, DateTime.UtcNow);
            var episode = CreateEpisode(102, TranslationState.AwaitingSource, DateTime.UtcNow);
            seedContext.Movies.Add(movie);
            seedContext.Episodes.Add(episode);
            await seedContext.SaveChangesAsync();
            movieId = movie.Id;
            episodeId = episode.Id;
        }

        await RunStartupAsync(connection);

        await using (var context = new LingarrDbContext(options))
        {
            Assert.Equal(
                TranslationState.Unknown,
                (await context.Movies.SingleAsync(movie => movie.Id == movieId)).TranslationState);
            Assert.Equal(
                TranslationState.Unknown,
                (await context.Episodes.SingleAsync(episode => episode.Id == episodeId)).TranslationState);

            var version = await context.Settings.SingleAsync(
                setting => setting.Key == SettingKeys.Translation.SubtitleStateEvaluatorVersion);
            Assert.Equal(
                StartupService.CurrentSubtitleStateEvaluatorVersion.ToString(),
                version.Value);

            // Restore the persisted AwaitingSource states: the stored evaluator version must prevent a second reset.
            (await context.Movies.SingleAsync(movie => movie.Id == movieId)).TranslationState =
                TranslationState.AwaitingSource;
            (await context.Episodes.SingleAsync(episode => episode.Id == episodeId)).TranslationState =
                TranslationState.AwaitingSource;
            await context.SaveChangesAsync();
        }

        await RunStartupAsync(connection);

        await using (var context = new LingarrDbContext(options))
        {
            Assert.Equal(
                TranslationState.AwaitingSource,
                (await context.Movies.SingleAsync(movie => movie.Id == movieId)).TranslationState);
            Assert.Equal(
                TranslationState.AwaitingSource,
                (await context.Episodes.SingleAsync(episode => episode.Id == episodeId)).TranslationState);
        }
    }

    [Fact]
    public async Task StartAsync_ReEvaluationSkipsFilelessAndCompleteRows()
    {
        using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var options = CreateSqliteOptions(connection);

        int filelessMovieId;
        int completeMovieId;
        int unindexedMovieId;
        int staleMovieId;
        await using (var seedContext = new LingarrDbContext(options))
        {
            await seedContext.Database.EnsureCreatedAsync();
            var filelessMovie = CreateMovie(201, null, TranslationState.AwaitingSource, DateTime.UtcNow);
            var completeMovie = CreateMovie(202, "complete.mkv", TranslationState.Complete, DateTime.UtcNow);
            var unindexedMovie = CreateMovie(203, "unindexed.mkv", TranslationState.AwaitingSource, null);
            var staleMovie = CreateMovie(204, "stale.mkv", TranslationState.AwaitingSource, DateTime.UtcNow);
            seedContext.Movies.AddRange(filelessMovie, completeMovie, unindexedMovie, staleMovie);
            await seedContext.SaveChangesAsync();
            filelessMovieId = filelessMovie.Id;
            completeMovieId = completeMovie.Id;
            unindexedMovieId = unindexedMovie.Id;
            staleMovieId = staleMovie.Id;
        }

        await RunStartupAsync(connection);

        await using (var context = new LingarrDbContext(options))
        {
            Assert.Equal(
                TranslationState.AwaitingSource,
                (await context.Movies.SingleAsync(movie => movie.Id == filelessMovieId)).TranslationState);
            Assert.Equal(
                TranslationState.Complete,
                (await context.Movies.SingleAsync(movie => movie.Id == completeMovieId)).TranslationState);
            Assert.Equal(
                TranslationState.AwaitingSource,
                (await context.Movies.SingleAsync(movie => movie.Id == unindexedMovieId)).TranslationState);
            Assert.Equal(
                TranslationState.Unknown,
                (await context.Movies.SingleAsync(movie => movie.Id == staleMovieId)).TranslationState);
        }
    }

    private static DbContextOptions<LingarrDbContext> CreateSqliteOptions(SqliteConnection connection)
    {
        return new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(connection)
            .Options;
    }

    private static async Task RunStartupAsync(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddDbContext<LingarrDbContext>(builder => builder.UseSqlite(connection));
        using var provider = services.BuildServiceProvider();
        var service = new StartupService(provider, NullLogger<StartupService>.Instance);
        await service.StartAsync(CancellationToken.None);
    }

    private static Movie CreateMovie(
        int radarrId,
        string? fileName,
        TranslationState state,
        DateTime? indexedAt)
    {
        var movie = new Movie
        {
            RadarrId = radarrId,
            Title = $"Movie {radarrId}",
            Path = $"/movies/movie-{radarrId}",
            FileName = fileName,
            DateAdded = DateTime.UtcNow,
            TranslationState = state,
            IndexedAt = indexedAt
        };
        movie.EmbeddedSubtitles.Add(CreateBitmapSubtitle());
        return movie;
    }

    private static Episode CreateEpisode(
        int sonarrId,
        TranslationState state,
        DateTime? indexedAt)
    {
        var show = new Show
        {
            SonarrId = sonarrId,
            Title = $"Show {sonarrId}",
            Path = $"/shows/show-{sonarrId}",
            DateAdded = DateTime.UtcNow
        };
        var season = new Season
        {
            SeasonNumber = 1,
            Show = show
        };
        var episode = new Episode
        {
            SonarrId = sonarrId,
            EpisodeNumber = 1,
            Title = $"Episode {sonarrId}",
            Path = $"/shows/show-{sonarrId}/s01e01.mkv",
            FileName = "s01e01.mkv",
            Season = season,
            TranslationState = state,
            IndexedAt = indexedAt
        };
        episode.EmbeddedSubtitles.Add(CreateBitmapSubtitle());
        return episode;
    }

    private static EmbeddedSubtitle CreateBitmapSubtitle()
    {
        return new EmbeddedSubtitle
        {
            StreamIndex = 0,
            Language = "eng",
            Title = "English",
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false
        };
    }
}
