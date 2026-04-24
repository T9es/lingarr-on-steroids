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
