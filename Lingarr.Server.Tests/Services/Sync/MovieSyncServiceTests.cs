using System;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services.Sync;
using Lingarr.Server.Services.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Sync;

public class MovieSyncServiceTests
{
    [Fact]
    public async Task RemoveNonExistentMovies_EmptyMovie_IsMarkedMissingAndKept()
    {
        await using var context = BuildContext();
        var movie = await SeedMovieAsync(context, "Movie A");
        var service = CreateService(context);

        await service.RemoveNonExistentMovies([], "default");

        var saved = await context.Movies.SingleAsync();
        Assert.NotNull(saved.MissingSince);
        Assert.Equal(movie.Id, saved.Id);
    }

    [Fact]
    public async Task RemoveNonExistentMovies_ExistingMissingSince_IsPreserved()
    {
        await using var context = BuildContext();
        var firstObserved = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        var movie = await SeedMovieAsync(context, "Movie A");
        movie.MissingSince = firstObserved;
        await context.SaveChangesAsync();
        var service = CreateService(context);

        await service.RemoveNonExistentMovies([], "default");

        Assert.Equal(firstObserved, movie.MissingSince);
    }

    [Fact]
    public async Task RemoveNonExistentMovies_MovieWithHistory_StaysVisible()
    {
        await using var context = BuildContext();
        var movie = await SeedMovieAsync(context, "Movie A");
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
        var service = CreateService(context);

        await service.RemoveNonExistentMovies([], "default");

        var saved = await context.Movies.SingleAsync();
        Assert.Null(saved.MissingSince);
    }

    [Fact]
    public async Task RemoveNonExistentMovies_MovieStillInRadarr_IsNotMarkedMissing()
    {
        await using var context = BuildContext();
        var movie = await SeedMovieAsync(context, "Movie A");
        var service = CreateService(context);

        await service.RemoveNonExistentMovies([movie.RadarrId], "default");

        Assert.Null(movie.MissingSince);
        Assert.Equal(1, await context.Movies.CountAsync());
    }

    private static MovieSyncService CreateService(LingarrDbContext context)
    {
        return new MovieSyncService(
            context,
            Mock.Of<IMovieSync>(),
            NullLogger<MovieSyncService>.Instance);
    }

    private static async Task<Movie> SeedMovieAsync(LingarrDbContext context, string title)
    {
        var movie = new Movie
        {
            RadarrId = 100,
            Title = title,
            FileName = "movie",
            Path = "/movies/movie",
            DateAdded = DateTime.UtcNow.AddDays(-10),
            SourceInstanceId = "default",
            TranslationState = TranslationState.AwaitingSource
        };
        context.Movies.Add(movie);
        await context.SaveChangesAsync();
        return movie;
    }

    private static LingarrDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LingarrDbContext(options);
    }
}
