using System;
using System.Linq;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class BlockedMediaServiceTests
{
    [Fact]
    public async Task GetBlockedMediaAsync_ReturnsOcrBlockedItemsWithBlockedStreamDetail()
    {
        // Arrange
        await using var context = BuildContext();

        var movie = new Movie
        {
            RadarrId = 1,
            Title = "Blocked Movie",
            FileName = "movie.mkv",
            Path = "C:\\media",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.OcrBlocked
        };
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            StreamIndex = 1,
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.Succeeded,
            OcrQualityScore = 100
        });
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            StreamIndex = 2,
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.BlockedLowQuality,
            OcrQualityScore = 38,
            OcrIssueSummary = "Too few cues (12) detected for a full episode."
        });
        context.Movies.Add(movie);
        await context.SaveChangesAsync();

        var service = new BlockedMediaService(context);

        // Act
        var result = await service.GetBlockedMediaAsync();

        // Assert
        var item = Assert.Single(result);
        Assert.Equal(movie.Id, item.MediaId);
        Assert.Equal("movie", item.MediaType);
        Assert.Equal("Blocked Movie", item.Title);
        Assert.Equal(TranslationState.OcrBlocked, item.TranslationState);
        Assert.Equal(2, item.StreamIndex);
        Assert.Equal(SubtitleOcrStatus.BlockedLowQuality, item.OcrStatus);
        Assert.Equal(38, item.OcrQualityScore);
        Assert.Equal("Too few cues (12) detected for a full episode.", item.OcrIssueSummary);
        Assert.Null(item.LastSubtitleCheckAt);
    }

    [Fact]
    public async Task GetBlockedMediaAsync_ReturnsAwaitingSourceItemsWithLastSubtitleCheckAt()
    {
        // Arrange
        await using var context = BuildContext();

        var show = new Show
        {
            SonarrId = 1,
            Title = "Show",
            Path = "C:\\shows\\show",
            DateAdded = DateTime.UtcNow
        };
        var season = new Season
        {
            SeasonNumber = 1,
            Show = show
        };
        var episode = new Episode
        {
            SonarrId = 1,
            Title = "Episode 1",
            EpisodeNumber = 1,
            Season = season,
            TranslationState = TranslationState.AwaitingSource,
            LastSubtitleCheckAt = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc)
        };
        context.Episodes.Add(episode);
        await context.SaveChangesAsync();

        var service = new BlockedMediaService(context);

        // Act
        var result = await service.GetBlockedMediaAsync();

        // Assert
        var item = Assert.Single(result);
        Assert.Equal(episode.Id, item.MediaId);
        Assert.Equal("episode", item.MediaType);
        Assert.Equal("Episode 1", item.Title);
        Assert.Equal(TranslationState.AwaitingSource, item.TranslationState);
        Assert.Equal(new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc), item.LastSubtitleCheckAt);
        Assert.Null(item.StreamIndex);
        Assert.Null(item.OcrQualityScore);
    }

    [Fact]
    public async Task GetBlockedMediaAsync_OrdersByStateThenTitle()
    {
        // Arrange
        await using var context = BuildContext();

        context.Movies.Add(new Movie
        {
            RadarrId = 1,
            Title = "Zeta Movie",
            FileName = "z.mkv",
            Path = "C:\\media",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.Stale
        });
        context.Movies.Add(new Movie
        {
            RadarrId = 2,
            Title = "Alpha Movie",
            FileName = "a.mkv",
            Path = "C:\\media",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.OcrBlocked
        });
        context.Movies.Add(new Movie
        {
            RadarrId = 3,
            Title = "Beta Movie",
            FileName = "b.mkv",
            Path = "C:\\media",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.AwaitingSource
        });
        await context.SaveChangesAsync();

        var service = new BlockedMediaService(context);

        // Act
        var result = await service.GetBlockedMediaAsync();

        // Assert
        Assert.Equal(new[] { "Alpha Movie", "Zeta Movie", "Beta Movie" }, result.Select(i => i.Title));
        Assert.Equal(
            new[] { TranslationState.OcrBlocked, TranslationState.Stale, TranslationState.AwaitingSource },
            result.Select(i => i.TranslationState));
    }

    [Fact]
    public async Task GetBlockedMediaAsync_ExcludesStatesOutsideBlockedSet()
    {
        // Arrange
        await using var context = BuildContext();

        context.Movies.Add(new Movie
        {
            RadarrId = 1,
            Title = "Pending Movie",
            FileName = "p.mkv",
            Path = "C:\\media",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.Pending
        });
        context.Movies.Add(new Movie
        {
            RadarrId = 2,
            Title = "Complete Movie",
            FileName = "c.mkv",
            Path = "C:\\media",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.Complete
        });
        await context.SaveChangesAsync();

        var service = new BlockedMediaService(context);

        // Act
        var result = await service.GetBlockedMediaAsync();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetBlockedMediaAsync_AppliesLimit()
    {
        // Arrange
        await using var context = BuildContext();

        for (var i = 0; i < 5; i++)
        {
            context.Movies.Add(new Movie
            {
                RadarrId = i + 1,
                Title = $"Movie {i}",
                FileName = $"{i}.mkv",
                Path = "C:\\media",
                DateAdded = DateTime.UtcNow,
                TranslationState = TranslationState.AwaitingSource
            });
        }
        await context.SaveChangesAsync();

        var service = new BlockedMediaService(context);

        // Act
        var result = await service.GetBlockedMediaAsync(limit: 2);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetBlockedMediaAsync_OcrBlockedWithoutBlockedStream_LeavesOcrFieldsNull()
    {
        // Arrange
        await using var context = BuildContext();

        context.Movies.Add(new Movie
        {
            RadarrId = 1,
            Title = "No Stream Movie",
            FileName = "ns.mkv",
            Path = "C:\\media",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.OcrBlocked
        });
        await context.SaveChangesAsync();

        var service = new BlockedMediaService(context);

        // Act
        var result = await service.GetBlockedMediaAsync();

        // Assert
        var item = Assert.Single(result);
        Assert.Equal(TranslationState.OcrBlocked, item.TranslationState);
        Assert.Null(item.StreamIndex);
        Assert.Null(item.OcrStatus);
        Assert.Null(item.OcrQualityScore);
        Assert.Null(item.OcrIssueSummary);
    }

    [Fact]
    public async Task GetBlockedMediaAsync_OcrBlockedFallsBackToBitmapStreamWhenNoQualityVerdict()
    {
        // Arrange
        await using var context = BuildContext();

        var movie = new Movie
        {
            RadarrId = 1,
            Title = "Fallback Movie",
            FileName = "fb.mkv",
            Path = "C:\\media",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.OcrBlocked
        };
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            StreamIndex = 3,
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.NotStarted
        });
        context.Movies.Add(movie);
        await context.SaveChangesAsync();

        var service = new BlockedMediaService(context);

        // Act
        var result = await service.GetBlockedMediaAsync();

        // Assert
        var item = Assert.Single(result);
        Assert.Equal(3, item.StreamIndex);
        Assert.Equal(SubtitleOcrStatus.NotStarted, item.OcrStatus);
    }

    private static LingarrDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LingarrDbContext(options);
    }
}
