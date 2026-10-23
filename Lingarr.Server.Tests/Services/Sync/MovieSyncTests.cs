using System;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Sync;
using Lingarr.Server.Models.Integrations;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Sync;

public class MovieSyncTests
{
    [Fact]
    public async Task SyncMovie_WhenExistingRadarrMovieHasNoFile_MarksRowAwaitingSourceWithoutQueueing()
    {
        await using var context = BuildContext();
        var movie = new Movie
        {
            Id = 1,
            RadarrId = 42,
            Title = "Old Title",
            FileName = "Old.File",
            Path = "/media/filmy/Old Movie",
            DateAdded = DateTime.UtcNow.AddDays(-10),
            SourceInstanceId = "default",
            TranslationState = TranslationState.Pending,
            StateSettingsVersion = 1,
            IndexedAt = null,
            MediaHash = "old-hash",
            EmbeddedSubtitles =
            [
                new EmbeddedSubtitle
                {
                    StreamIndex = 3,
                    Language = "eng",
                    CodecName = "ass",
                    IsTextBased = true
                }
            ]
        };
        context.Movies.Add(movie);
        await context.SaveChangesAsync();

        var mediaStateServiceMock = new Mock<IMediaStateService>();
        mediaStateServiceMock
            .Setup(s => s.GetSettingsVersionAsync())
            .ReturnsAsync(7);

        var extractionServiceMock = new Mock<ISubtitleExtractionService>();
        var service = CreateService(context, mediaStateServiceMock, extractionServiceMock);

        var synced = await service.SyncMovie(new RadarrMovie
        {
            Id = 42,
            Title = "The Movie",
            Path = "/media/filmy/The Movie (2024) {tmdb-123}",
            RootFolderPath = "/media/filmy",
            Added = DateTime.UtcNow.ToString("O"),
            HasFile = false
        }, "default");
        await context.SaveChangesAsync();

        Assert.NotNull(synced);
        var saved = await context.Movies
            .Include(m => m.EmbeddedSubtitles)
            .SingleAsync(m => m.RadarrId == 42);
        Assert.Equal("The Movie", saved.Title);
        Assert.Equal("/media/filmy/The Movie (2024) {tmdb-123}", saved.Path);
        Assert.Null(saved.FileName);
        Assert.Equal(string.Empty, saved.MediaHash);
        Assert.Equal(TranslationState.AwaitingSource, saved.TranslationState);
        Assert.NotNull(saved.IndexedAt);
        Assert.NotNull(saved.LastSubtitleCheckAt);
        Assert.Equal(7, saved.StateSettingsVersion);
        Assert.Empty(saved.EmbeddedSubtitles);
        extractionServiceMock.Verify(s => s.SyncEmbeddedSubtitles(It.IsAny<Movie>()), Times.Never);
        mediaStateServiceMock.Verify(
            s => s.UpdateStateAsync(It.IsAny<Movie>(), It.IsAny<MediaType>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncMovie_WhenEmptyMovieIsMissing_StampsMissingSinceOnce()
    {
        await using var context = BuildContext();
        var movie = CreateExistingMovie(43);
        context.Movies.Add(movie);
        await context.SaveChangesAsync();

        var mediaStateServiceMock = new Mock<IMediaStateService>();
        mediaStateServiceMock
            .Setup(s => s.GetSettingsVersionAsync())
            .ReturnsAsync(1);
        var service = CreateService(context, mediaStateServiceMock, new Mock<ISubtitleExtractionService>());

        await service.SyncMovie(CreateRadarrMovie(43, hasFile: false), "default");
        await context.SaveChangesAsync();

        var firstObserved = movie.MissingSince;
        Assert.NotNull(firstObserved);

        await service.SyncMovie(CreateRadarrMovie(43, hasFile: false), "default");
        await context.SaveChangesAsync();

        Assert.Equal(firstObserved, movie.MissingSince);
    }

    [Fact]
    public async Task SyncMovie_WhenFileReturns_ClearsMissingSince()
    {
        await using var context = BuildContext();
        var movie = CreateExistingMovie(44);
        movie.MissingSince = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        context.Movies.Add(movie);
        await context.SaveChangesAsync();

        var mediaStateServiceMock = new Mock<IMediaStateService>();
        mediaStateServiceMock
            .Setup(s => s.GetSettingsVersionAsync())
            .ReturnsAsync(1);
        var service = CreateService(context, mediaStateServiceMock, new Mock<ISubtitleExtractionService>());

        await service.SyncMovie(CreateRadarrMovie(44, hasFile: true), "default");
        await context.SaveChangesAsync();

        Assert.Null(movie.MissingSince);
    }

    [Fact]
    public async Task SyncMovie_WhenMissingMovieHasTranslationHistory_DoesNotStampMissingSince()
    {
        await using var context = BuildContext();
        var movie = CreateExistingMovie(45);
        context.Movies.Add(movie);
        await context.SaveChangesAsync();
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

        var mediaStateServiceMock = new Mock<IMediaStateService>();
        mediaStateServiceMock
            .Setup(s => s.GetSettingsVersionAsync())
            .ReturnsAsync(1);
        var service = CreateService(context, mediaStateServiceMock, new Mock<ISubtitleExtractionService>());

        await service.SyncMovie(CreateRadarrMovie(45, hasFile: false), "default");
        await context.SaveChangesAsync();

        Assert.Null(movie.MissingSince);
    }

    [Fact]
    public async Task SyncMovie_WhenMissingMovieIsPrioritized_DoesNotStampMissingSince()
    {
        await using var context = BuildContext();
        var movie = CreateExistingMovie(46);
        movie.IsPriority = true;
        context.Movies.Add(movie);
        await context.SaveChangesAsync();

        var mediaStateServiceMock = new Mock<IMediaStateService>();
        mediaStateServiceMock
            .Setup(s => s.GetSettingsVersionAsync())
            .ReturnsAsync(1);
        var service = CreateService(context, mediaStateServiceMock, new Mock<ISubtitleExtractionService>());

        await service.SyncMovie(CreateRadarrMovie(46, hasFile: false), "default");
        await context.SaveChangesAsync();

        Assert.Null(movie.MissingSince);
    }

    private static Movie CreateExistingMovie(int radarrId)
    {
        return new Movie
        {
            RadarrId = radarrId,
            Title = $"Movie {radarrId}",
            FileName = $"movie-{radarrId}",
            Path = $"/media/filmy/Movie {radarrId}",
            DateAdded = DateTime.UtcNow.AddDays(-10),
            SourceInstanceId = "default",
            TranslationState = TranslationState.AwaitingSource
        };
    }

    private static RadarrMovie CreateRadarrMovie(int radarrId, bool hasFile)
    {
        return new RadarrMovie
        {
            Id = radarrId,
            Title = $"Movie {radarrId}",
            Path = $"/media/filmy/Movie {radarrId} (2024) {{tmdb-{radarrId}}}",
            RootFolderPath = "/media/filmy",
            Added = DateTime.UtcNow.AddDays(-10).ToString("O"),
            HasFile = hasFile,
            MovieFile = hasFile
                ? new RadarrMovieFile { Path = $"/media/filmy/Movie {radarrId}/movie-{radarrId}.mkv" }
                : new RadarrMovieFile()
        };
    }

    private static MovieSync CreateService(
        LingarrDbContext context,
        Mock<IMediaStateService> mediaStateServiceMock,
        Mock<ISubtitleExtractionService> extractionServiceMock)
    {
        return new MovieSync(
            context,
            new PathConversionService(context),
            NullLogger<MovieSync>.Instance,
            Mock.Of<IImageSync>(),
            extractionServiceMock.Object,
            mediaStateServiceMock.Object,
            Mock.Of<IOrphanSubtitleCleanupService>());
    }

    private static LingarrDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LingarrDbContext(options);
    }
}
