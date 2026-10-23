using System;
using System.Linq;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class BlockedMediaServiceTests
{
    [Theory]
    [InlineData("PgsToSrt produced no output file.")]
    [InlineData(null)]
    [InlineData("")]
    public async Task GetBlockedMediaAsync_WithFailedOcr_DoesNotExposeStaleSuccessfulQuality(string? error)
    {
        await using var context = BuildContext();
        var movie = new Movie
        {
            RadarrId = 31,
            Title = "Failed OCR",
            FileName = "failed.mkv",
            Path = "/media/movies",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.OcrBlocked
        };
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            StreamIndex = 0,
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.Failed,
            OcrQualityScore = 100,
            OcrIssueSummary = "OCR passed quality checks.",
            OcrError = error
        });
        context.Movies.Add(movie);
        await context.SaveChangesAsync();

        var page = await CreateService(context).GetBlockedMediaAsync();
        var item = Assert.Single(page.Items);

        Assert.Null(item.OcrQualityScore);
        Assert.NotEqual("OCR passed quality checks.", item.OcrIssueSummary);
        if (!string.IsNullOrWhiteSpace(error))
        {
            Assert.Equal(error, item.OcrIssueSummary);
        }
        else
        {
            Assert.Contains("failed", item.OcrIssueSummary!, StringComparison.OrdinalIgnoreCase);
        }
    }

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

        var service = CreateService(context);

        // Act
        var page = await service.GetBlockedMediaAsync();

        // Assert
        var item = Assert.Single(page.Items);
        Assert.Equal(movie.Id, item.MediaId);
        Assert.Equal("movie", item.MediaType);
        Assert.Equal("Blocked Movie", item.Title);
        Assert.Equal(TranslationState.OcrBlocked, item.TranslationState);
        Assert.Equal(2, item.StreamIndex);
        Assert.Equal(SubtitleOcrStatus.BlockedLowQuality, item.OcrStatus);
        Assert.Equal(38, item.OcrQualityScore);
        Assert.Equal("Too few cues (12) detected for a full episode.", item.OcrIssueSummary);
        Assert.Null(item.LastSubtitleCheckAt);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(1, page.CountsByState["OcrBlocked"]);
    }

    [Fact]
    public async Task GetBlockedMediaAsync_PreservesStreamIndexZero()
    {
        // Arrange
        await using var context = BuildContext();

        var movie = new Movie
        {
            RadarrId = 2,
            Title = "Stream Zero Movie",
            FileName = "zero.mkv",
            Path = "C:\\media",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.OcrBlocked
        };
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            StreamIndex = 0,
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.BlockedLowQuality,
            OcrQualityScore = 30
        });
        context.Movies.Add(movie);
        await context.SaveChangesAsync();

        // Act
        var page = await CreateService(context).GetBlockedMediaAsync();

        // Assert
        var item = Assert.Single(page.Items);
        Assert.Equal(0, item.StreamIndex);
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

        var service = CreateService(context);

        // Act
        var page = await service.GetBlockedMediaAsync();

        // Assert
        var item = Assert.Single(page.Items);
        Assert.Equal(episode.Id, item.MediaId);
        Assert.Equal("episode", item.MediaType);
        Assert.Equal("Episode 1", item.Title);
        Assert.Equal(TranslationState.AwaitingSource, item.TranslationState);
        Assert.Equal(new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc), item.LastSubtitleCheckAt);
        Assert.Equal(new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc), item.LastEvaluatedAt);
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

        var service = CreateService(context);

        // Act
        var page = await service.GetBlockedMediaAsync();

        // Assert
        Assert.Equal(new[] { "Alpha Movie", "Zeta Movie", "Beta Movie" }, page.Items.Select(i => i.Title));
        Assert.Equal(
            new[] { TranslationState.OcrBlocked, TranslationState.Stale, TranslationState.AwaitingSource },
            page.Items.Select(i => i.TranslationState));
    }

    [Fact]
    public async Task GetBlockedMediaAsync_OrdersFailedStateBetweenOcrBlockedAndStale()
    {
        // Arrange
        await using var context = BuildContext();

        context.Movies.Add(new Movie
        {
            RadarrId = 1,
            Title = "Stale Movie",
            FileName = "s.mkv",
            Path = "C:\\media",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.Stale
        });
        context.Movies.Add(new Movie
        {
            RadarrId = 2,
            Title = "Failed Movie",
            FileName = "f.mkv",
            Path = "C:\\media",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.Failed
        });
        context.Movies.Add(new Movie
        {
            RadarrId = 3,
            Title = "Ocr Movie",
            FileName = "o.mkv",
            Path = "C:\\media",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.OcrBlocked
        });
        await context.SaveChangesAsync();

        // Act
        var page = await CreateService(context).GetBlockedMediaAsync();

        // Assert
        Assert.Equal(
            new[] { "Ocr Movie", "Failed Movie", "Stale Movie" },
            page.Items.Select(i => i.Title));
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
        context.Movies.Add(new Movie
        {
            RadarrId = 3,
            Title = "OcrPending Movie",
            FileName = "op.mkv",
            Path = "C:\\media",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.OcrPending
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);

        // Act
        var page = await service.GetBlockedMediaAsync();

        // Assert
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task GetBlockedMediaAsync_ExcludesMissingMediaRows()
    {
        // Arrange
        await using var context = BuildContext();

        context.Movies.Add(new Movie
        {
            RadarrId = 1,
            Title = "Present Movie",
            FileName = "present.mkv",
            Path = "C:\\media",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.AwaitingSource
        });
        context.Movies.Add(new Movie
        {
            RadarrId = 2,
            Title = "Missing Movie",
            FileName = "missing.mkv",
            Path = "C:\\media",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.AwaitingSource,
            MissingSince = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        await context.SaveChangesAsync();

        // Act
        var page = await CreateService(context).GetBlockedMediaAsync();

        // Assert
        var item = Assert.Single(page.Items);
        Assert.Equal("Present Movie", item.Title);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(1, page.CountsByState["AwaitingSource"]);
    }

    [Fact]
    public async Task GetBlockedMediaAsync_AppliesPaginationAndReportsTotalCount()
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

        var service = CreateService(context);

        // Act
        var firstPage = await service.GetBlockedMediaAsync(page: 1, pageSize: 2);
        var secondPage = await service.GetBlockedMediaAsync(page: 2, pageSize: 2);
        var thirdPage = await service.GetBlockedMediaAsync(page: 3, pageSize: 2);
        var clampedPage = await service.GetBlockedMediaAsync(page: 99, pageSize: 2);

        // Assert
        Assert.Equal(5, firstPage.TotalCount);
        Assert.Equal(2, firstPage.Items.Count());
        Assert.Equal(1, firstPage.PageNumber);
        Assert.Equal(2, firstPage.PageSize);
        Assert.Equal(new[] { "Movie 0", "Movie 1" }, firstPage.Items.Select(i => i.Title));
        Assert.Equal(new[] { "Movie 2", "Movie 3" }, secondPage.Items.Select(i => i.Title));
        Assert.Equal(new[] { "Movie 4" }, thirdPage.Items.Select(i => i.Title));
        Assert.Equal(5, secondPage.TotalCount);
        Assert.Equal(3, clampedPage.PageNumber);
        Assert.Equal(new[] { "Movie 4" }, clampedPage.Items.Select(i => i.Title));
        Assert.Equal(5, firstPage.CountsByState["AwaitingSource"]);
    }

    [Fact]
    public async Task GetBlockedMediaAsync_AppliesStateFilterAndKeepsFullStateCounts()
    {
        // Arrange
        await using var context = BuildContext();

        context.Movies.Add(new Movie
        {
            RadarrId = 1,
            Title = "Awaiting One",
            FileName = "a1.mkv",
            Path = "C:\\media",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.AwaitingSource
        });
        context.Movies.Add(new Movie
        {
            RadarrId = 2,
            Title = "Awaiting Two",
            FileName = "a2.mkv",
            Path = "C:\\media",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.AwaitingSource
        });
        var ocrMovie = new Movie
        {
            RadarrId = 3,
            Title = "Ocr One",
            FileName = "o1.mkv",
            Path = "C:\\media",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.OcrBlocked
        };
        ocrMovie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            StreamIndex = 0,
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.BlockedLowQuality,
            OcrQualityScore = 30
        });
        context.Movies.Add(ocrMovie);
        await context.SaveChangesAsync();

        // Act
        var page = await CreateService(context).GetBlockedMediaAsync(
            state: TranslationState.OcrBlocked);

        // Assert
        var item = Assert.Single(page.Items);
        Assert.Equal("Ocr One", item.Title);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(2, page.CountsByState["AwaitingSource"]);
        Assert.Equal(1, page.CountsByState["OcrBlocked"]);
    }

    [Fact]
    public async Task GetBlockedMediaAsync_IncludesFailedItemsWithLatestFailureMessage()
    {
        // Arrange
        await using var context = BuildContext();
        var failedAt = new DateTime(2026, 10, 20, 8, 30, 0, DateTimeKind.Utc);

        var movie = new Movie
        {
            RadarrId = 1,
            Title = "Failed Movie",
            FileName = "failed.mkv",
            Path = "C:\\media",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.Failed
        };
        context.Movies.Add(movie);
        await context.SaveChangesAsync();

        var request = new TranslationRequest
        {
            Title = "Failed Movie",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Failed,
            FailedAt = failedAt
        };
        context.TranslationRequests.Add(request);
        await context.SaveChangesAsync();

        context.TranslationRequestLogs.Add(new TranslationRequestLog
        {
            TranslationRequestId = request.Id,
            Level = "Error",
            Message = "Translation failed",
            Details = "No suitable source subtitle matched source language 'en'."
        });
        context.TranslationRequestLogs.Add(new TranslationRequestLog
        {
            TranslationRequestId = request.Id,
            Level = "Information",
            Message = "Older informational log",
            Details = "Should be ignored."
        });
        await context.SaveChangesAsync();

        // Act
        var page = await CreateService(context).GetBlockedMediaAsync();

        // Assert
        var item = Assert.Single(page.Items);
        Assert.Equal(TranslationState.Failed, item.TranslationState);
        Assert.Equal(
            "No suitable source subtitle matched source language 'en'.",
            item.FailureMessage);
        Assert.Equal(failedAt, item.LastEvaluatedAt);
        Assert.Equal(1, page.CountsByState["Failed"]);
    }

    [Fact]
    public async Task GetBlockedMediaAsync_ReDerivesIssueTextWhenScoreMeetsCurrentThreshold()
    {
        // Arrange
        await using var context = BuildContext();
        var completedAt = new DateTime(2026, 5, 10, 21, 16, 50, DateTimeKind.Utc);

        var movie = new Movie
        {
            RadarrId = 1,
            Title = "Threshold Changed",
            FileName = "threshold.mkv",
            Path = "C:\\media",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.OcrBlocked
        };
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            StreamIndex = 0,
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.BlockedLowQuality,
            OcrQualityScore = 75,
            OcrIssueSummary = "17 % of cues are empty. Quality score 75 is below the required 80.",
            OcrCompletedAt = completedAt
        });
        context.Movies.Add(movie);
        await context.SaveChangesAsync();

        // Act: current threshold is 70, so the stored score now passes.
        var page = await CreateService(context, minQualityScore: 70).GetBlockedMediaAsync();

        // Assert
        var item = Assert.Single(page.Items);
        Assert.True(item.OcrPassesCurrentThreshold);
        Assert.Equal(70, item.OcrMinimumQualityScore);
        Assert.Equal(75, item.OcrQualityScore);
        Assert.Contains("meets the current minimum of 70", item.OcrIssueSummary!);
        Assert.DoesNotContain("below the required 80", item.OcrIssueSummary!);
        Assert.Equal(completedAt, item.LastEvaluatedAt);
    }

    [Fact]
    public async Task GetBlockedMediaAsync_KeepsStoredIssueTextWhenScoreIsBelowCurrentThreshold()
    {
        // Arrange
        await using var context = BuildContext();

        var movie = new Movie
        {
            RadarrId = 1,
            Title = "Still Below Threshold",
            FileName = "below.mkv",
            Path = "C:\\media",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.OcrBlocked
        };
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            StreamIndex = 0,
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.BlockedLowQuality,
            OcrQualityScore = 30,
            OcrIssueSummary = "Too few cues (12) detected for a full episode."
        });
        context.Movies.Add(movie);
        await context.SaveChangesAsync();

        // Act
        var page = await CreateService(context, minQualityScore: 70).GetBlockedMediaAsync();

        // Assert
        var item = Assert.Single(page.Items);
        Assert.False(item.OcrPassesCurrentThreshold);
        Assert.Equal(70, item.OcrMinimumQualityScore);
        Assert.Equal("Too few cues (12) detected for a full episode.", item.OcrIssueSummary);
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

        var service = CreateService(context);

        // Act
        var page = await service.GetBlockedMediaAsync();

        // Assert
        var item = Assert.Single(page.Items);
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

        var service = CreateService(context);

        // Act
        var page = await service.GetBlockedMediaAsync();

        // Assert
        var item = Assert.Single(page.Items);
        Assert.Equal(3, item.StreamIndex);
        Assert.Equal(SubtitleOcrStatus.NotStarted, item.OcrStatus);
    }

    [Fact]
    public async Task RecheckMediaAsync_WhenMovieExists_StampsCheckTimeAndReturnsNewState()
    {
        // Arrange
        await using var context = BuildContext();

        var movie = new Movie
        {
            RadarrId = 1,
            Title = "Recheck Movie",
            FileName = "recheck.mkv",
            Path = "C:\\media",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.Failed
        };
        context.Movies.Add(movie);
        await context.SaveChangesAsync();

        var mediaStateService = new Mock<IMediaStateService>();
        mediaStateService
            .Setup(service => service.UpdateStateAsync(
                It.IsAny<IMedia>(),
                MediaType.Movie,
                It.IsAny<bool>()))
            .ReturnsAsync(TranslationState.Pending);

        // Act
        var result = await CreateService(context, mediaStateService: mediaStateService)
            .RecheckMediaAsync(MediaType.Movie, movie.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(movie.Id, result!.MediaId);
        Assert.Equal("movie", result.MediaType);
        Assert.Equal(TranslationState.Pending, result.State);
        Assert.NotEqual(default, result.EvaluatedAt);
        Assert.Equal(result.EvaluatedAt, movie.LastSubtitleCheckAt);
        mediaStateService.Verify(
            service => service.UpdateStateAsync(
                It.IsAny<IMedia>(),
                MediaType.Movie,
                It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task RecheckMediaAsync_WhenMovieDoesNotExist_ReturnsNull()
    {
        // Arrange
        await using var context = BuildContext();

        // Act
        var result = await CreateService(context).RecheckMediaAsync(MediaType.Movie, 12345);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task RecheckMediaAsync_WithNonMediaType_Throws()
    {
        // Arrange
        await using var context = BuildContext();

        // Act and Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            CreateService(context).RecheckMediaAsync(MediaType.Show, 1));
    }

    private static BlockedMediaService CreateService(
        LingarrDbContext context,
        int minQualityScore = 80,
        Mock<IMediaStateService>? mediaStateService = null)
    {
        var settingService = new Mock<ISettingService>();
        settingService
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrMinQualityScore))
            .ReturnsAsync(minQualityScore.ToString());

        return new BlockedMediaService(
            context,
            settingService.Object,
            (mediaStateService ?? new Mock<IMediaStateService>()).Object);
    }

    private static LingarrDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LingarrDbContext(options);
    }
}
