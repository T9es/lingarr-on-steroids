using System;
using System.Linq;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Services.Media;
using Lingarr.Server.Services.Sync;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lingarr.Server.Tests.Services.Sync;

public class MissingMediaPrunePolicyTests
{
    [Theory]
    [InlineData(null, MissingMediaPrunePolicy.DefaultPruneWindowDays)]
    [InlineData("", MissingMediaPrunePolicy.DefaultPruneWindowDays)]
    [InlineData("not-a-number", MissingMediaPrunePolicy.DefaultPruneWindowDays)]
    [InlineData("-4", MissingMediaPrunePolicy.DefaultPruneWindowDays)]
    [InlineData("0", 0)]
    [InlineData("3", 3)]
    [InlineData("14", 14)]
    public void ResolvePruneWindowDays_ParsesSettingWithDefaults(string? settingValue, int expected)
    {
        Assert.Equal(expected, MissingMediaPrunePolicy.ResolvePruneWindowDays(settingValue));
    }

    [Fact]
    public async Task IsEligibleForMissingTrackingAsync_EmptyMovie_IsEligible()
    {
        await using var context = BuildContext();
        var movie = await SeedMovieAsync(context);

        Assert.True(await MissingMediaPrunePolicy.IsEligibleForMissingTrackingAsync(context, movie));
    }

    [Theory]
    [InlineData("excluded")]
    [InlineData("priority")]
    [InlineData("age-threshold")]
    public async Task IsEligibleForMissingTrackingAsync_MovieWithPerItemSettings_IsNotEligible(string setting)
    {
        await using var context = BuildContext();
        var movie = await SeedMovieAsync(context);
        switch (setting)
        {
            case "excluded":
                movie.ExcludeFromTranslation = true;
                break;
            case "priority":
                movie.IsPriority = true;
                break;
            default:
                movie.TranslationAgeThreshold = 12;
                break;
        }

        await context.SaveChangesAsync();

        Assert.False(await MissingMediaPrunePolicy.IsEligibleForMissingTrackingAsync(context, movie));
    }

    [Fact]
    public async Task IsEligibleForMissingTrackingAsync_MovieWithTranslationRequest_IsNotEligible()
    {
        await using var context = BuildContext();
        var movie = await SeedMovieAsync(context);
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

        Assert.False(await MissingMediaPrunePolicy.IsEligibleForMissingTrackingAsync(context, movie));
    }

    [Fact]
    public async Task IsEligibleForMissingTrackingAsync_MovieWithDiagnostic_IsNotEligible()
    {
        await using var context = BuildContext();
        var movie = await SeedMovieAsync(context);
        context.TranslationDiagnosticEvents.Add(new TranslationDiagnosticEvent
        {
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Title = movie.Title,
            Stage = "translate",
            ReasonCode = "test",
            Summary = "test",
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        });
        await context.SaveChangesAsync();

        Assert.False(await MissingMediaPrunePolicy.IsEligibleForMissingTrackingAsync(context, movie));
    }

    [Fact]
    public async Task IsEligibleForMissingTrackingAsync_MovieWithEmbeddedSubtitle_IsNotEligible()
    {
        await using var context = BuildContext();
        var movie = await SeedMovieAsync(context);
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            StreamIndex = 0,
            Language = "eng",
            CodecName = "subrip",
            IsTextBased = true
        });
        await context.SaveChangesAsync();

        Assert.False(await MissingMediaPrunePolicy.IsEligibleForMissingTrackingAsync(context, movie));
    }

    [Fact]
    public async Task IsEligibleForMissingTrackingAsync_EmptyEpisode_IsEligible()
    {
        await using var context = BuildContext();
        var episode = await SeedEpisodeAsync(context);

        Assert.True(await MissingMediaPrunePolicy.IsEligibleForMissingTrackingAsync(context, episode));
    }

    [Theory]
    [InlineData("episode-excluded")]
    [InlineData("season-excluded")]
    [InlineData("show-excluded")]
    [InlineData("show-priority")]
    [InlineData("show-age-threshold")]
    public async Task IsEligibleForMissingTrackingAsync_EpisodeWithPerItemSettings_IsNotEligible(string setting)
    {
        await using var context = BuildContext();
        var episode = await SeedEpisodeAsync(context);
        switch (setting)
        {
            case "episode-excluded":
                episode.ExcludeFromTranslation = true;
                break;
            case "season-excluded":
                episode.Season.ExcludeFromTranslation = true;
                break;
            case "show-excluded":
                episode.Season.Show.ExcludeFromTranslation = true;
                break;
            case "show-priority":
                episode.Season.Show.IsPriority = true;
                break;
            default:
                episode.Season.Show.TranslationAgeThreshold = 6;
                break;
        }

        await context.SaveChangesAsync();

        Assert.False(await MissingMediaPrunePolicy.IsEligibleForMissingTrackingAsync(context, episode));
    }

    [Fact]
    public async Task IsEligibleForMissingTrackingAsync_EpisodeWithEmbeddedSubtitle_IsNotEligible()
    {
        await using var context = BuildContext();
        var episode = await SeedEpisodeAsync(context);
        episode.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            StreamIndex = 0,
            Language = "eng",
            CodecName = "subrip",
            IsTextBased = true
        });
        await context.SaveChangesAsync();

        Assert.False(await MissingMediaPrunePolicy.IsEligibleForMissingTrackingAsync(context, episode));
    }

    [Fact]
    public async Task WhereNotMissing_ExcludesMissingRowsFromBothMediaTypes()
    {
        await using var context = BuildContext();
        var missingMovie = await SeedMovieAsync(context);
        missingMovie.MissingSince = DateTime.UtcNow.AddDays(-2);
        var presentMovie = await SeedMovieAsync(context);
        var missingEpisode = await SeedEpisodeAsync(context);
        missingEpisode.MissingSince = DateTime.UtcNow.AddDays(-2);
        var presentEpisode = await SeedEpisodeAsync(context);
        await context.SaveChangesAsync();

        var movies = await context.Movies.AsNoTracking().WhereNotMissing().Select(movie => movie.Id).ToListAsync();
        var episodes = await context.Episodes.AsNoTracking().WhereNotMissing().Select(episode => episode.Id).ToListAsync();

        Assert.Equal(new[] { presentMovie.Id }, movies);
        Assert.Equal(new[] { presentEpisode.Id }, episodes);
    }

    private static async Task<Movie> SeedMovieAsync(LingarrDbContext context)
    {
        var movie = new Movie
        {
            RadarrId = Random.Shared.Next(1, 1_000_000),
            Title = "Movie",
            FileName = "movie",
            Path = "/movies/movie",
            DateAdded = DateTime.UtcNow.AddDays(-10),
            SourceInstanceId = "default"
        };
        context.Movies.Add(movie);
        await context.SaveChangesAsync();
        return movie;
    }

    private static async Task<Episode> SeedEpisodeAsync(LingarrDbContext context)
    {
        var show = new Show
        {
            SonarrId = Random.Shared.Next(1, 1_000_000),
            Title = "Show",
            Path = "/shows/show",
            DateAdded = DateTime.UtcNow.AddDays(-10),
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
            Title = "Episode",
            FileName = "episode",
            Path = "/shows/show",
            DateAdded = DateTime.UtcNow.AddDays(-10),
            Season = season,
            SourceInstanceId = "default"
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
