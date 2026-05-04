using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Hubs;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Subtitle;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class TranslationRequestServiceTests
{
    [Fact]
    public async Task DedupeQueuedRequests_RemovesDuplicatePendingRequests()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var requests = new List<TranslationRequest>
        {
            CreateRequest(1, 10, MediaType.Movie, "en", "ro", "/movies/a.en.srt", TranslationStatus.Pending, now),
            CreateRequest(2, 10, MediaType.Movie, "en", "ro", "/movies/a.en.srt", TranslationStatus.Pending, now.AddSeconds(1)),
            CreateRequest(3, 10, MediaType.Movie, "en", "ro", "/movies/a.en.srt", TranslationStatus.Pending, now.AddSeconds(2)),
            CreateRequest(4, 11, MediaType.Movie, "en", "ro", "/movies/b.en.srt", TranslationStatus.Pending, now),
            CreateRequest(5, 11, MediaType.Movie, "en", "ro", "/movies/b.en.srt", TranslationStatus.Pending, now.AddSeconds(1))
        };

        context.TranslationRequests.AddRange(requests);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var (removed, skipped) = await service.DedupeQueuedRequests();

        Assert.Equal(3, removed);
        Assert.Equal(0, skipped);

        var remaining = await context.TranslationRequests
            .Where(tr => tr.Status == TranslationStatus.Pending)
            .ToListAsync();

        Assert.Equal(2, remaining.Count);
        Assert.Contains(remaining, tr => tr.MediaId == 10 && tr.SubtitleToTranslate == "/movies/a.en.srt");
        Assert.Contains(remaining, tr => tr.MediaId == 11 && tr.SubtitleToTranslate == "/movies/b.en.srt");
    }

    [Fact]
    public async Task DedupeQueuedRequests_RemovesQueuedRequestsWithDifferentRequiredOutputFormats()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var workloadKey = $"library:{MediaType.Movie}:210";

        var assPrimary = CreateRequest(1, 210, MediaType.Movie, "en", "pl", "/movies/a.en.ass", TranslationStatus.Pending, now);
        assPrimary.WorkloadItemKey = workloadKey;
        assPrimary.SourceSubtitleFormat = ".ass";
        assPrimary.RequiredOutputFormats = ".ass";
        assPrimary.IsActive = true;

        var assDuplicate = CreateRequest(2, 210, MediaType.Movie, "en", "pl", "/movies/a.en.ass", TranslationStatus.Pending, now.AddSeconds(1));
        assDuplicate.WorkloadItemKey = workloadKey;
        assDuplicate.SourceSubtitleFormat = ".ass";
        assDuplicate.RequiredOutputFormats = ".ass";
        assDuplicate.IsActive = true;

        var srtRequest = CreateRequest(3, 210, MediaType.Movie, "en", "pl", "/movies/a.en.ass", TranslationStatus.Pending, now.AddSeconds(2));
        srtRequest.WorkloadItemKey = workloadKey;
        srtRequest.SourceSubtitleFormat = ".ass";
        srtRequest.RequiredOutputFormats = ".srt";
        srtRequest.IsActive = true;

        context.TranslationRequests.AddRange(assPrimary, assDuplicate, srtRequest);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var (removed, skipped) = await service.DedupeQueuedRequests();

        Assert.Equal(2, removed);
        Assert.Equal(0, skipped);

        var remaining = await context.TranslationRequests
            .Where(tr => tr.WorkloadItemKey == workloadKey && tr.Status == TranslationStatus.Pending)
            .OrderBy(tr => tr.Id)
            .ToListAsync();

        Assert.Single(remaining);
        Assert.Equal(".ass", remaining[0].RequiredOutputFormats);
    }

    [Fact]
    public async Task ReenqueueQueuedRequests_SignalsWorkerService()
    {
        // With the new worker-based system, ReenqueueQueuedRequests signals the worker
        // to pick up pending jobs
        await using var context = BuildContext();

        var now = DateTime.UtcNow;

        var normalMovie = new Movie
        {
            Id = 10,
            RadarrId = 10,
            Title = "Normal Movie",
            FileName = "normal.mkv",
            Path = "/movies/normal.mkv",
            DateAdded = now.AddDays(-2),
            IsPriority = false
        };

        var priorityMovie = new Movie
        {
            Id = 11,
            RadarrId = 11,
            Title = "Priority Movie",
            FileName = "priority.mkv",
            Path = "/movies/priority.mkv",
            DateAdded = now.AddDays(-2),
            IsPriority = true,
            PriorityDate = now
        };

        context.Movies.AddRange(normalMovie, priorityMovie);

        var requests = new List<TranslationRequest>
        {
            CreateRequest(1, normalMovie.Id, MediaType.Movie, "en", "ro", "/movies/normal.en.srt",
                TranslationStatus.Pending, now),
            CreateRequest(2, priorityMovie.Id, MediaType.Movie, "en", "ro", "/movies/priority.en.srt",
                TranslationStatus.Pending, now.AddSeconds(1))
        };

        context.TranslationRequests.AddRange(requests);
        await context.SaveChangesAsync();

        var workerServiceMock = new Mock<ITranslationWorkerService>();
        var signalCallCount = 0;
        workerServiceMock.Setup(w => w.Signal()).Callback(() => signalCallCount++);

        var service = CreateService(context, workerServiceMock);

        await service.ReenqueueQueuedRequests();

        // Verify Signal was called
        Assert.True(signalCallCount >= 1, "Worker service Signal should be called");
        
        // Verify all requests are still pending
        var pendingCount = await context.TranslationRequests
            .CountAsync(tr => tr.Status == TranslationStatus.Pending);
        Assert.Equal(2, pendingCount);
    }

    [Fact]
    public async Task RefreshPriorityForMedia_Show_UpdatesPendingEpisodeRequestsForShow()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var context = BuildSqliteContext(connection);

        var now = DateTime.UtcNow;
        var show = CreateShowWithEpisodes(100, "Target Show", false, now, 201, 202);
        context.Shows.Add(show);

        var firstRequest = CreateRequest(
            1,
            201,
            MediaType.Episode,
            "en",
            "pl",
            "/shows/target/s01e01.en.srt",
            TranslationStatus.Pending,
            now);
        firstRequest.IsPriority = true;

        var secondRequest = CreateRequest(
            2,
            202,
            MediaType.Episode,
            "en",
            "pl",
            "/shows/target/s01e02.en.srt",
            TranslationStatus.Pending,
            now.AddSeconds(1));
        secondRequest.IsPriority = true;

        context.TranslationRequests.AddRange(firstRequest, secondRequest);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var updated = await service.RefreshPriorityForMedia(MediaType.Show, show.Id);

        Assert.Equal(2, updated);
        context.ChangeTracker.Clear();

        var requests = await context.TranslationRequests
            .Where(request => request.MediaType == MediaType.Episode)
            .OrderBy(request => request.Id)
            .ToListAsync();

        Assert.All(requests, request => Assert.False(request.IsPriority));
    }

    [Fact]
    public async Task RefreshPriorityForMedia_Show_DoesNotUpdateOtherShows()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var context = BuildSqliteContext(connection);

        var now = DateTime.UtcNow;
        var targetShow = CreateShowWithEpisodes(110, "Target Show", false, now, 211);
        var otherShow = CreateShowWithEpisodes(111, "Other Show", false, now, 212);
        context.Shows.AddRange(targetShow, otherShow);

        var targetRequest = CreateRequest(
            1,
            211,
            MediaType.Episode,
            "en",
            "pl",
            "/shows/target/s01e01.en.srt",
            TranslationStatus.Pending,
            now);
        targetRequest.IsPriority = true;

        var otherRequest = CreateRequest(
            2,
            212,
            MediaType.Episode,
            "en",
            "pl",
            "/shows/other/s01e01.en.srt",
            TranslationStatus.Pending,
            now);
        otherRequest.IsPriority = true;

        context.TranslationRequests.AddRange(targetRequest, otherRequest);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var updated = await service.RefreshPriorityForMedia(MediaType.Show, targetShow.Id);

        Assert.Equal(1, updated);
        context.ChangeTracker.Clear();

        var refreshedTarget = await context.TranslationRequests.SingleAsync(request => request.Id == targetRequest.Id);
        var untouchedOther = await context.TranslationRequests.SingleAsync(request => request.Id == otherRequest.Id);

        Assert.False(refreshedTarget.IsPriority);
        Assert.True(untouchedOther.IsPriority);
    }

    [Fact]
    public async Task RefreshPriorityForMedia_Show_LeavesInProgressRequestsUnchanged()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var context = BuildSqliteContext(connection);

        var now = DateTime.UtcNow;
        var show = CreateShowWithEpisodes(120, "Target Show", false, now, 221, 222);
        context.Shows.Add(show);

        var pendingRequest = CreateRequest(
            1,
            221,
            MediaType.Episode,
            "en",
            "pl",
            "/shows/target/s01e01.en.srt",
            TranslationStatus.Pending,
            now);
        pendingRequest.IsPriority = true;

        var inProgressRequest = CreateRequest(
            2,
            222,
            MediaType.Episode,
            "en",
            "pl",
            "/shows/target/s01e02.en.srt",
            TranslationStatus.InProgress,
            now);
        inProgressRequest.IsPriority = true;

        context.TranslationRequests.AddRange(pendingRequest, inProgressRequest);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var updated = await service.RefreshPriorityForMedia(MediaType.Show, show.Id);

        Assert.Equal(1, updated);
        context.ChangeTracker.Clear();

        var refreshedPending = await context.TranslationRequests.SingleAsync(request => request.Id == pendingRequest.Id);
        var untouchedInProgress = await context.TranslationRequests.SingleAsync(request => request.Id == inProgressRequest.Id);

        Assert.False(refreshedPending.IsPriority);
        Assert.True(untouchedInProgress.IsPriority);
    }

    [Fact]
    public async Task InterruptActiveRequestsForMedia_MarksRequestsInterruptedAndClearsMediaHash()
    {
        await using var context = BuildContext();

        var movie = new Movie
        {
            Id = 50,
            RadarrId = 50,
            Title = "Movie",
            FileName = "movie.mkv",
            Path = "/movies",
            MediaHash = "stale-hash",
            DateAdded = DateTime.UtcNow.AddDays(-1)
        };

        context.Movies.Add(movie);
        context.TranslationRequests.AddRange(
            CreateRequest(1, 50, MediaType.Movie, "en", "pl", "/movies/movie.en.srt", TranslationStatus.Pending, DateTime.UtcNow.AddMinutes(-2)),
            CreateRequest(2, 50, MediaType.Movie, "en", "de", "/movies/movie.en.srt", TranslationStatus.InProgress, DateTime.UtcNow.AddMinutes(-1)),
            CreateRequest(3, 50, MediaType.Movie, "en", "fr", "/movies/movie.en.srt", TranslationStatus.Completed, DateTime.UtcNow.AddMinutes(-3)));
        await context.SaveChangesAsync();

        var cancellationMock = new Mock<ITranslationCancellationService>();
        var mediaStateMock = new Mock<IMediaStateService>();
        var service = CreateService(
            context,
            cancellationServiceMock: cancellationMock,
            mediaStateServiceMock: mediaStateMock);

        var interrupted = await service.InterruptActiveRequestsForMedia(MediaType.Movie, 50);

        Assert.Equal(2, interrupted);

        var requests = await context.TranslationRequests.OrderBy(r => r.Id).ToListAsync();
        Assert.Equal(TranslationStatus.Interrupted, requests[0].Status);
        Assert.Equal(TranslationStatus.Interrupted, requests[1].Status);
        Assert.Equal(TranslationStatus.Completed, requests[2].Status);
        Assert.Null(requests[0].IsActive);
        Assert.Null(requests[1].IsActive);

        var updatedMovie = await context.Movies.FindAsync(50);
        Assert.NotNull(updatedMovie);
        Assert.Equal(string.Empty, updatedMovie!.MediaHash);

        cancellationMock.Verify(c => c.CancelJob(1), Times.Once);
        cancellationMock.Verify(c => c.CancelJob(2), Times.Once);
        mediaStateMock.Verify(m => m.UpdateStateAsync(It.IsAny<Movie>(), MediaType.Movie, true), Times.Once);
    }

    [Fact]
    public async Task InterruptActiveRequestsForMedia_IgnoresUploadRequestsWithCollidingMediaId()
    {
        await using var context = BuildContext();

        context.Movies.Add(new Movie
        {
            Id = 55,
            RadarrId = 55,
            Title = "Movie 55",
            FileName = "movie-55.mkv",
            Path = "/movies",
            MediaHash = "hash",
            DateAdded = DateTime.UtcNow
        });

        context.TranslationRequests.AddRange(
            CreateRequest(1, 55, MediaType.Movie, "en", "pl", "/movies/movie.en.srt", TranslationStatus.Pending, DateTime.UtcNow.AddMinutes(-2)),
            new TranslationRequest
            {
                Id = 2,
                MediaId = 55,
                MediaType = MediaType.Movie,
                WorkloadKind = TranslationWorkloadKind.Upload,
                UploadBatchFileId = 55,
                Title = "Upload 55",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                SubtitleToTranslate = "/uploads/file.en.srt",
                Status = TranslationStatus.Pending,
                IsActive = true
            });
        await context.SaveChangesAsync();

        var cancellationMock = new Mock<ITranslationCancellationService>();
        var mediaStateMock = new Mock<IMediaStateService>();
        var service = CreateService(
            context,
            cancellationServiceMock: cancellationMock,
            mediaStateServiceMock: mediaStateMock);

        var interrupted = await service.InterruptActiveRequestsForMedia(MediaType.Movie, 55);

        Assert.Equal(1, interrupted);

        var requests = await context.TranslationRequests.OrderBy(r => r.Id).ToListAsync();
        Assert.Equal(TranslationStatus.Interrupted, requests[0].Status);
        Assert.Equal(TranslationStatus.Pending, requests[1].Status);
        Assert.Null(requests[0].IsActive);
        Assert.True(requests[1].IsActive);
    }

    [Fact]
    public async Task CreateRequest_DedupesActiveRequestsAcrossDifferentRequiredOutputFormats()
    {
        await using var context = BuildContext();

        var movie = new Movie
        {
            Id = 60,
            RadarrId = 60,
            Title = "Format Aware Movie",
            FileName = "format-aware.mkv",
            Path = "/movies",
            DateAdded = DateTime.UtcNow
        };

        context.Movies.Add(movie);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var createdAt = DateTime.UtcNow;

        var assRequest = CreateRequest(1, movie.Id, MediaType.Movie, "en", "pl", "/movies/movie.en.ass",
            TranslationStatus.Pending, createdAt);
        assRequest.RequiredOutputFormats = ".ass";

        var srtRequest = CreateRequest(2, movie.Id, MediaType.Movie, "en", "pl", "/movies/movie.en.ass",
            TranslationStatus.Pending, createdAt.AddSeconds(1));
        srtRequest.RequiredOutputFormats = ".srt";

        var assId = await service.CreateRequest(assRequest);
        var srtId = await service.CreateRequest(srtRequest);

        Assert.Equal(assId, srtId);
        Assert.Equal(1, await context.TranslationRequests.CountAsync());
    }

    [Fact]
    public async Task CreateRequest_AllowsPrimaryAndSupplementalActiveRequestsForSameTarget()
    {
        using var connection = new SqliteConnection("Filename=:memory:");
        connection.Open();
        await using var context = BuildSqliteContext(connection);

        var movie = new Movie
        {
            Id = 61,
            RadarrId = 61,
            Title = "Supplemental Movie",
            FileName = "supplemental.mkv",
            Path = "/movies",
            DateAdded = DateTime.UtcNow
        };

        context.Movies.Add(movie);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var createdAt = DateTime.UtcNow;

        var primaryRequest = CreateRequest(
            1,
            movie.Id,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/movie.en.srt",
            TranslationStatus.Pending,
            createdAt);
        primaryRequest.SourceSubtitleType = SubtitleLanguageHelper.TypeFull;
        primaryRequest.WorkloadItemKey = $"library:{MediaType.Movie}:{movie.Id}";

        var forcedRequest = CreateRequest(
            2,
            movie.Id,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/movie.en.srt",
            TranslationStatus.Pending,
            createdAt.AddSeconds(1));
        forcedRequest.SourceSubtitleType = SubtitleLanguageHelper.TypeForced;
        forcedRequest.IsForcedSubtitle = true;
        forcedRequest.SourceSnapshotIdentity = "embedded|en|stream:3|codec:ass";
        forcedRequest.SourceSnapshotStreamIndex = 3;
        forcedRequest.WorkloadItemKey = $"library:{MediaType.Movie}:{movie.Id}";

        var primaryId = await service.CreateRequest(primaryRequest);
        var forcedId = await service.CreateRequest(forcedRequest);

        Assert.NotEqual(0, primaryId);
        Assert.NotEqual(0, forcedId);
        Assert.NotEqual(primaryId, forcedId);
        Assert.Equal(2, await context.TranslationRequests.CountAsync());
    }

    [Theory]
    [InlineData("srt-only", ".srt")]
    [InlineData("both", ".ass,.srt")]
    public async Task CreateRequest_DedupesLegacyActiveRequestUsingSubtitleOutputModeWhenRequiredFormatsAreMissing(
        string subtitleOutputMode,
        string expectedRequiredOutputFormats)
    {
        await using var context = BuildContext();

        var movie = new Movie
        {
            Id = 62,
            RadarrId = 62,
            Title = "Legacy Format Aware Movie",
            FileName = "legacy-format-aware.mkv",
            Path = "/movies",
            DateAdded = DateTime.UtcNow
        };

        context.Movies.Add(movie);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var createdAt = DateTime.UtcNow;
        var workloadKey = $"library:{MediaType.Movie}:{movie.Id}";

        var legacyActiveRequest = CreateRequest(
            1,
            movie.Id,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/movie.en.ass",
            TranslationStatus.Pending,
            createdAt);
        legacyActiveRequest.WorkloadItemKey = workloadKey;
        legacyActiveRequest.SourceSubtitleFormat = ".ass";
        legacyActiveRequest.SubtitleOutputMode = subtitleOutputMode;
        legacyActiveRequest.RequiredOutputFormats = null;
        legacyActiveRequest.IsActive = true;

        context.TranslationRequests.Add(legacyActiveRequest);
        await context.SaveChangesAsync();

        var newRequest = CreateRequest(
            2,
            movie.Id,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/movie.en.ass",
            TranslationStatus.Pending,
            createdAt.AddSeconds(1));
        newRequest.WorkloadItemKey = workloadKey;
        newRequest.SourceSubtitleFormat = ".ass";
        newRequest.SubtitleOutputMode = subtitleOutputMode;
        newRequest.RequiredOutputFormats = expectedRequiredOutputFormats;

        var requestId = await service.CreateRequest(newRequest);

        Assert.Equal(legacyActiveRequest.Id, requestId);
        Assert.Equal(1, await context.TranslationRequests.CountAsync());
    }

    [Theory]
    [InlineData("subrip", ".srt")]
    [InlineData("mov_text", ".srt")]
    [InlineData("webvtt", ".vtt")]
    [InlineData("ass", ".ass")]
    [InlineData("ssa", ".ssa")]
    public async Task CreateRequest_NormalizesEmbeddedCodecNames(string codecName, string expectedFormat)
    {
        await using var context = BuildContext();

        var movie = new Movie
        {
            Id = 61,
            RadarrId = 61,
            Title = "Codec Name Movie",
            FileName = "codec-name.mkv",
            Path = "/movies",
            DateAdded = DateTime.UtcNow
        };

        context.Movies.Add(movie);
        await context.SaveChangesAsync();

        var settingServiceMock = new Mock<ISettingService>();
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync("match-source");

        var service = CreateService(context, settingServiceMock: settingServiceMock);

        var requestId = await service.CreateRequest(new TranslateAbleSubtitle
        {
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            SubtitlePath = null,
            SubtitleFormat = codecName,
            SourceLanguage = "en",
            TargetLanguage = "pl"
        });

        var request = await context.TranslationRequests.SingleAsync(tr => tr.Id == requestId);
        Assert.Equal(expectedFormat, request.SourceSubtitleFormat);
        Assert.Equal(expectedFormat, request.RequiredOutputFormats);
    }

    [Fact]
    public async Task CreateRequest_DedupesWithinCustomSourceWorkloadButNotAcrossWorkloadKinds()
    {
        await using var context = BuildContext();

        var customSource = new CustomSource
        {
            Id = 70,
            Name = "Anime Archive",
            SourceType = CustomSourceType.MovieRoot,
            RootPath = "/custom/anime",
            Recursive = true,
            Enabled = true,
            IncludeInAutomation = true
        };

        var customItem = new CustomMediaItem
        {
            Id = 71,
            CustomSourceId = customSource.Id,
            CustomSource = customSource,
            ItemKind = CustomMediaItemKind.Movie,
            Title = "My Custom Movie",
            FileName = "movie.mkv",
            Path = "/custom/anime/movie.mkv",
            RelativePath = "movie.mkv",
            DateAdded = DateTime.UtcNow
        };

        var libraryMovie = new Movie
        {
            Id = 72,
            RadarrId = 72,
            Title = "Library Movie",
            FileName = "library.mkv",
            Path = "/movies",
            DateAdded = DateTime.UtcNow
        };

        context.CustomSources.Add(customSource);
        context.CustomMediaItems.Add(customItem);
        context.Movies.Add(libraryMovie);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var customRequest = new TranslationRequest
        {
            Title = customItem.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = "/custom/anime/movie.en.srt",
            SourceSubtitleFormat = ".srt",
            RequiredOutputFormats = ".srt",
            WorkloadKind = TranslationWorkloadKind.CustomSource,
            CustomMediaItemId = customItem.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            IsActive = true
        };

        var duplicateCustomRequest = new TranslationRequest
        {
            Title = customItem.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = "/custom/anime/movie.en.srt",
            SourceSubtitleFormat = ".srt",
            RequiredOutputFormats = ".srt",
            WorkloadKind = TranslationWorkloadKind.CustomSource,
            CustomMediaItemId = customItem.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            IsActive = true
        };

        var libraryRequest = new TranslationRequest
        {
            MediaId = libraryMovie.Id,
            Title = libraryMovie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = "/movies/library.en.srt",
            SourceSubtitleFormat = ".srt",
            RequiredOutputFormats = ".srt",
            WorkloadKind = TranslationWorkloadKind.Library,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            IsActive = true
        };

        var firstId = await service.CreateRequest(customRequest);
        var duplicateId = await service.CreateRequest(duplicateCustomRequest);
        var libraryId = await service.CreateRequest(libraryRequest);

        Assert.Equal(firstId, duplicateId);
        Assert.NotEqual(firstId, libraryId);
        Assert.Equal(2, await context.TranslationRequests.CountAsync());
    }

    [Fact]
    public async Task RetryAllFailedRequests_IgnoresFutureNextRetryAt()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var failedRequest = CreateRequest(
            1,
            300,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/movie.en.srt",
            TranslationStatus.Failed,
            now.AddMinutes(-2));
        failedRequest.WorkloadItemKey = $"library:{MediaType.Movie}:300";
        failedRequest.IsActive = null;
        failedRequest.NextRetryAt = now.AddHours(2);

        context.TranslationRequests.Add(failedRequest);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.RetryAllFailedRequests();

        Assert.Equal(1, result.TotalFailed);
        Assert.Equal(1, result.Retried);
        Assert.Equal(0, result.BlockedByActiveRequest);
        Assert.Equal(0, result.RemainingFailed);

        var updatedRequest = await context.TranslationRequests.SingleAsync(tr => tr.Id == failedRequest.Id);
        Assert.Equal(TranslationStatus.Pending, updatedRequest.Status);
        Assert.True(updatedRequest.IsActive);
        Assert.Null(updatedRequest.NextRetryAt);
    }

    [Fact]
    public async Task GetOverview_ReturnsCountsAndLimitsFailedAndInProgressItems()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var requests = new List<TranslationRequest>
        {
            CreateRequest(1, 100, MediaType.Movie, "en", "pl", "/movies/pending-1.en.srt", TranslationStatus.Pending, now),
            CreateRequest(2, 101, MediaType.Movie, "en", "pl", "/movies/pending-2.en.srt", TranslationStatus.Pending, now.AddSeconds(1)),
            CreateRequest(3, 102, MediaType.Movie, "en", "pl", "/movies/failed-1.en.srt", TranslationStatus.Failed, now.AddSeconds(2)),
            CreateRequest(4, 103, MediaType.Movie, "en", "pl", "/movies/failed-2.en.srt", TranslationStatus.Failed, now.AddSeconds(3)),
            CreateRequest(5, 104, MediaType.Movie, "en", "pl", "/movies/failed-3.en.srt", TranslationStatus.Failed, now.AddSeconds(4)),
            CreateRequest(6, 105, MediaType.Movie, "en", "pl", "/movies/progress-1.en.srt", TranslationStatus.InProgress, now.AddSeconds(5)),
            CreateRequest(7, 106, MediaType.Movie, "en", "pl", "/movies/progress-2.en.srt", TranslationStatus.Paused, now.AddSeconds(6)),
            CreateRequest(8, 107, MediaType.Movie, "en", "pl", "/movies/done.en.srt", TranslationStatus.Completed, now.AddSeconds(7))
        };

        requests[2].CompletedAt = now.AddMinutes(-1);
        requests[3].CompletedAt = now.AddMinutes(-2);
        requests[4].CompletedAt = now.AddMinutes(-3);
        requests[7].CompletedAt = now;

        context.TranslationRequests.AddRange(requests);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var overview = await service.GetOverview(null, "CreatedAt", true, 1, 20, 2);

        Assert.Equal(4, overview.ActiveCount);
        Assert.Equal(2, overview.Pending.TotalCount);
        Assert.Equal(2, overview.Pending.Items.Count());
        Assert.Equal(3, overview.Failed.TotalCount);
        Assert.Equal(2, overview.Failed.Items.Count);
        Assert.Equal(2, overview.InProgress.TotalCount);
        Assert.Equal(2, overview.InProgress.Items.Count);
        Assert.DoesNotContain(overview.Failed.Items, request => request.Status != TranslationStatus.Failed);
        Assert.DoesNotContain(overview.InProgress.Items, request =>
            request.Status != TranslationStatus.InProgress && request.Status != TranslationStatus.Paused);
    }

    [Fact]
    public async Task GetOverview_UsesPendingPaginationAndSearch()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var alpha = CreateRequest(1, 100, MediaType.Movie, "en", "pl", "/movies/alpha.en.srt", TranslationStatus.Pending, now);
        alpha.Title = "Alpha Movie";
        var beta = CreateRequest(2, 101, MediaType.Movie, "en", "pl", "/movies/beta.en.srt", TranslationStatus.Pending, now.AddSeconds(1));
        beta.Title = "Beta Movie";
        var gamma = CreateRequest(3, 102, MediaType.Movie, "en", "pl", "/movies/gamma.en.srt", TranslationStatus.Pending, now.AddSeconds(2));
        gamma.Title = "Gamma Movie";

        context.TranslationRequests.AddRange(alpha, beta, gamma);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var overview = await service.GetOverview("movie", "Title", true, 2, 1, 25);

        Assert.Equal(3, overview.Pending.TotalCount);
        Assert.Equal(2, overview.Pending.PageNumber);
        Assert.Equal(1, overview.Pending.PageSize);
        var pendingItem = Assert.Single(overview.Pending.Items);
        Assert.Equal("Beta Movie", pendingItem.Title);
    }

    [Fact]
    public async Task RetryEligibleFailedRequests_RespectsFutureNextRetryAt()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var failedRequest = CreateRequest(
            1,
            305,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/movie.en.srt",
            TranslationStatus.Failed,
            now.AddMinutes(-2));
        failedRequest.WorkloadItemKey = $"library:{MediaType.Movie}:305";
        failedRequest.IsActive = null;
        failedRequest.NextRetryAt = now.AddHours(2);

        context.TranslationRequests.Add(failedRequest);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.RetryEligibleFailedRequests();

        Assert.Equal(0, result.TotalFailed);
        Assert.Equal(0, result.Retried);
        Assert.Equal(0, result.BlockedByActiveRequest);
        Assert.Equal(1, result.RemainingFailed);

        var updatedRequest = await context.TranslationRequests.SingleAsync(tr => tr.Id == failedRequest.Id);
        Assert.Equal(TranslationStatus.Failed, updatedRequest.Status);
        Assert.Null(updatedRequest.IsActive);
        Assert.Equal(failedRequest.NextRetryAt, updatedRequest.NextRetryAt);
    }

    [Fact]
    public async Task RetryAllFailedRequests_RetriesOnlyOneFromDuplicateFailedRowsWithinSameRun()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var workloadKey = $"library:{MediaType.Movie}:306";

        var failedFirst = CreateRequest(
            1,
            306,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/movie.en.srt",
            TranslationStatus.Failed,
            now.AddMinutes(-3));
        failedFirst.WorkloadItemKey = workloadKey;
        failedFirst.IsActive = null;
        failedFirst.NextRetryAt = now.AddHours(2);

        var failedSecond = CreateRequest(
            2,
            306,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/movie.en.srt",
            TranslationStatus.Failed,
            now.AddMinutes(-2));
        failedSecond.WorkloadItemKey = workloadKey;
        failedSecond.IsActive = null;
        failedSecond.NextRetryAt = null;

        context.TranslationRequests.AddRange(failedFirst, failedSecond);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.RetryAllFailedRequests();

        Assert.Equal(2, result.TotalFailed);
        Assert.Equal(1, result.Retried);
        Assert.Equal(1, result.BlockedByActiveRequest);
        Assert.Equal(1, result.RemainingFailed);

        var requests = await context.TranslationRequests
            .Where(tr => tr.WorkloadItemKey == workloadKey)
            .OrderBy(tr => tr.Id)
            .ToListAsync();

        Assert.Single(requests, tr => tr.Status == TranslationStatus.Pending);
        Assert.Single(requests, tr => tr.Status == TranslationStatus.Failed);
    }

    [Fact]
    public async Task RetryAllFailedRequests_RetriesOnlyOneFromLegacyKeylessDuplicateFailedRowsWithinSameRun()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var failedFirst = CreateRequest(
            1,
            307,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/movie.en.srt",
            TranslationStatus.Failed,
            now.AddMinutes(-3));
        failedFirst.WorkloadItemKey = string.Empty;
        failedFirst.IsActive = null;

        var failedSecond = CreateRequest(
            2,
            307,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/movie.en.srt",
            TranslationStatus.Failed,
            now.AddMinutes(-2));
        failedSecond.WorkloadItemKey = string.Empty;
        failedSecond.IsActive = null;

        context.TranslationRequests.AddRange(failedFirst, failedSecond);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.RetryAllFailedRequests();

        Assert.Equal(2, result.TotalFailed);
        Assert.Equal(1, result.Retried);
        Assert.Equal(1, result.BlockedByActiveRequest);
        Assert.Equal(1, result.RemainingFailed);

        var requests = await context.TranslationRequests
            .Where(tr => tr.MediaId == 307 && tr.MediaType == MediaType.Movie)
            .ToListAsync();

        Assert.Single(requests, tr => tr.Status == TranslationStatus.Pending);
        Assert.Single(requests, tr => tr.Status == TranslationStatus.Failed);
    }

    [Fact]
    public async Task RetryAllFailedRequests_RetriesLegacyKeylessCustomSourceMappedFromLibraryWithoutMediaId()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var failedLegacyCustom = new TranslationRequest
        {
            Id = 1,
            WorkloadKind = TranslationWorkloadKind.Library,
            WorkloadItemKey = string.Empty,
            MediaId = null,
            CustomMediaItemId = 901,
            MediaType = MediaType.Movie,
            Title = "Legacy custom request",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = "/custom/item.en.srt",
            SourceSubtitleFormat = ".srt",
            RequiredOutputFormats = ".srt",
            Status = TranslationStatus.Failed,
            IsActive = null,
            CreatedAt = now.AddMinutes(-2),
            UpdatedAt = now.AddMinutes(-2),
            NextRetryAt = now.AddHours(2)
        };

        context.TranslationRequests.Add(failedLegacyCustom);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.RetryAllFailedRequests();

        Assert.Equal(1, result.TotalFailed);
        Assert.Equal(1, result.Retried);
        Assert.Equal(0, result.BlockedByActiveRequest);
        Assert.Equal(0, result.RemainingFailed);

        var updatedRequest = await context.TranslationRequests.SingleAsync(tr => tr.Id == failedLegacyCustom.Id);
        Assert.Equal(TranslationStatus.Pending, updatedRequest.Status);
        Assert.True(updatedRequest.IsActive);
        Assert.Equal(TranslationWorkloadKind.CustomSource, updatedRequest.WorkloadKind);
        Assert.Equal("custom:901", updatedRequest.WorkloadItemKey);
    }

    [Fact]
    public async Task RetryAllFailedRequests_RetriesLegacyCustomSourceWithStaleNonEmptyWorkloadKey()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var failedLegacyCustom = new TranslationRequest
        {
            Id = 1,
            WorkloadKind = TranslationWorkloadKind.Library,
            WorkloadItemKey = "library:Movie:0",
            MediaId = null,
            CustomMediaItemId = 902,
            MediaType = MediaType.Movie,
            Title = "Legacy custom stale key",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = "/custom/stale.en.srt",
            SourceSubtitleFormat = ".srt",
            RequiredOutputFormats = ".srt",
            Status = TranslationStatus.Failed,
            IsActive = null,
            CreatedAt = now.AddMinutes(-2),
            UpdatedAt = now.AddMinutes(-2),
            NextRetryAt = now.AddHours(1)
        };

        context.TranslationRequests.Add(failedLegacyCustom);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.RetryAllFailedRequests();

        Assert.Equal(1, result.TotalFailed);
        Assert.Equal(1, result.Retried);
        Assert.Equal(0, result.BlockedByActiveRequest);
        Assert.Equal(0, result.RemainingFailed);

        var updatedRequest = await context.TranslationRequests.SingleAsync(tr => tr.Id == failedLegacyCustom.Id);
        Assert.Equal(TranslationStatus.Pending, updatedRequest.Status);
        Assert.True(updatedRequest.IsActive);
        Assert.Equal(TranslationWorkloadKind.CustomSource, updatedRequest.WorkloadKind);
        Assert.Equal("custom:902", updatedRequest.WorkloadItemKey);
    }

    [Fact]
    public async Task RetryAllFailedRequests_BlocksLegacyKeylessUploadWhenMatchingActiveExists()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var activeLegacyUpload = new TranslationRequest
        {
            Id = 1,
            WorkloadKind = TranslationWorkloadKind.Library,
            WorkloadItemKey = string.Empty,
            MediaId = null,
            UploadBatchFileId = 777,
            MediaType = MediaType.Movie,
            Title = "Legacy upload active",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = "/uploads/episode.en.srt",
            SourceSubtitleFormat = ".srt",
            RequiredOutputFormats = ".srt",
            Status = TranslationStatus.Pending,
            IsActive = true,
            CreatedAt = now.AddMinutes(-3),
            UpdatedAt = now.AddMinutes(-3)
        };

        var failedLegacyUpload = new TranslationRequest
        {
            Id = 2,
            WorkloadKind = TranslationWorkloadKind.Library,
            WorkloadItemKey = string.Empty,
            MediaId = null,
            UploadBatchFileId = 777,
            MediaType = MediaType.Movie,
            Title = "Legacy upload failed",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = "/uploads/episode.en.srt",
            SourceSubtitleFormat = ".srt",
            RequiredOutputFormats = ".srt",
            Status = TranslationStatus.Failed,
            IsActive = null,
            CreatedAt = now.AddMinutes(-2),
            UpdatedAt = now.AddMinutes(-2),
            NextRetryAt = null
        };

        context.TranslationRequests.AddRange(activeLegacyUpload, failedLegacyUpload);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.RetryAllFailedRequests();

        Assert.Equal(1, result.TotalFailed);
        Assert.Equal(0, result.Retried);
        Assert.Equal(1, result.BlockedByActiveRequest);
        Assert.Equal(1, result.RemainingFailed);

        var updatedRequest = await context.TranslationRequests.SingleAsync(tr => tr.Id == failedLegacyUpload.Id);
        Assert.Equal(TranslationStatus.Failed, updatedRequest.Status);
        Assert.Null(updatedRequest.IsActive);
    }

    [Fact]
    public async Task RetryAllFailedRequests_BlocksLegacyUploadWithStaleNonEmptyWorkloadKeyWhenCanonicalActiveExists()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var activeUpload = new TranslationRequest
        {
            Id = 1,
            WorkloadKind = TranslationWorkloadKind.Upload,
            WorkloadItemKey = "upload:778",
            MediaId = null,
            UploadBatchFileId = 778,
            MediaType = MediaType.Movie,
            Title = "Active upload canonical",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = "/uploads/canonical.en.srt",
            SourceSubtitleFormat = ".srt",
            RequiredOutputFormats = ".srt",
            Status = TranslationStatus.Pending,
            IsActive = true,
            CreatedAt = now.AddMinutes(-3),
            UpdatedAt = now.AddMinutes(-3)
        };

        var failedLegacyUpload = new TranslationRequest
        {
            Id = 2,
            WorkloadKind = TranslationWorkloadKind.Library,
            WorkloadItemKey = "library:Movie:0",
            MediaId = null,
            UploadBatchFileId = 778,
            MediaType = MediaType.Movie,
            Title = "Failed upload stale key",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = "/uploads/canonical.en.srt",
            SourceSubtitleFormat = ".srt",
            RequiredOutputFormats = ".srt",
            Status = TranslationStatus.Failed,
            IsActive = null,
            CreatedAt = now.AddMinutes(-2),
            UpdatedAt = now.AddMinutes(-2),
            NextRetryAt = null
        };

        context.TranslationRequests.AddRange(activeUpload, failedLegacyUpload);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.RetryAllFailedRequests();

        Assert.Equal(1, result.TotalFailed);
        Assert.Equal(0, result.Retried);
        Assert.Equal(1, result.BlockedByActiveRequest);
        Assert.Equal(1, result.RemainingFailed);

        var updatedRequest = await context.TranslationRequests.SingleAsync(tr => tr.Id == failedLegacyUpload.Id);
        Assert.Equal(TranslationStatus.Failed, updatedRequest.Status);
        Assert.Null(updatedRequest.IsActive);
    }

    [Fact]
    public async Task RetryAllFailedRequests_RetriesOnlyOneDuplicateWhenRowsSpanMultipleBatches()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var duplicateMediaId = 340;
        var duplicateWorkloadKey = $"library:{MediaType.Movie}:{duplicateMediaId}";
        var requests = new List<TranslationRequest>();

        var firstDuplicate = CreateRequest(
            1,
            duplicateMediaId,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/movie.en.srt",
            TranslationStatus.Failed,
            now.AddMinutes(-4));
        firstDuplicate.WorkloadItemKey = duplicateWorkloadKey;
        firstDuplicate.IsActive = null;
        requests.Add(firstDuplicate);

        for (var id = 2; id <= 220; id++)
        {
            var mediaId = 1000 + id;
            var request = CreateRequest(
                id,
                mediaId,
                MediaType.Movie,
                "en",
                "pl",
                $"/movies/movie-{mediaId}.en.srt",
                TranslationStatus.Failed,
                now.AddMinutes(-(220 - id)));
            request.WorkloadItemKey = $"library:{MediaType.Movie}:{mediaId}";
            request.IsActive = null;
            requests.Add(request);
        }

        var secondDuplicate = CreateRequest(
            221,
            duplicateMediaId,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/movie.en.srt",
            TranslationStatus.Failed,
            now.AddMinutes(-1));
        secondDuplicate.WorkloadItemKey = duplicateWorkloadKey;
        secondDuplicate.IsActive = null;
        requests.Add(secondDuplicate);

        context.TranslationRequests.AddRange(requests);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.RetryAllFailedRequests();

        Assert.Equal(requests.Count, result.TotalFailed);
        Assert.Equal(requests.Count - 1, result.Retried);
        Assert.Equal(1, result.BlockedByActiveRequest);
        Assert.Equal(1, result.RemainingFailed);

        var duplicateRequests = await context.TranslationRequests
            .Where(tr => tr.MediaId == duplicateMediaId && tr.MediaType == MediaType.Movie)
            .OrderBy(tr => tr.Id)
            .ToListAsync();

        Assert.Equal(2, duplicateRequests.Count);
        Assert.Single(duplicateRequests, tr => tr.Status == TranslationStatus.Pending);
        Assert.Single(duplicateRequests, tr => tr.Status == TranslationStatus.Failed);
    }

    [Fact]
    public async Task RetryAllFailedRequests_BlocksRetryWhenAnyActiveRequestExistsForWorkload()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var workloadKey = $"library:{MediaType.Movie}:310";

        var activeAss = CreateRequest(1, 310, MediaType.Movie, "en", "pl", "/movies/movie.en.ass", TranslationStatus.Pending, now.AddMinutes(-5));
        activeAss.WorkloadItemKey = workloadKey;
        activeAss.SourceSubtitleFormat = ".ass";
        activeAss.RequiredOutputFormats = ".ass";
        activeAss.IsActive = true;

        var failedAss = CreateRequest(2, 310, MediaType.Movie, "en", "pl", "/movies/movie.en.ass", TranslationStatus.Failed, now.AddMinutes(-4));
        failedAss.WorkloadItemKey = workloadKey;
        failedAss.SourceSubtitleFormat = ".ass";
        failedAss.RequiredOutputFormats = ".ass";
        failedAss.IsActive = null;
        failedAss.FailedAt = now.AddMinutes(-4);
        failedAss.NextRetryAt = null;

        var failedSrt = CreateRequest(3, 310, MediaType.Movie, "en", "pl", "/movies/movie.en.ass", TranslationStatus.Failed, now.AddMinutes(-3));
        failedSrt.WorkloadItemKey = workloadKey;
        failedSrt.SourceSubtitleFormat = ".ass";
        failedSrt.RequiredOutputFormats = ".srt";
        failedSrt.IsActive = null;
        failedSrt.FailedAt = now.AddMinutes(-3);
        failedSrt.NextRetryAt = null;

        context.TranslationRequests.AddRange(activeAss, failedAss, failedSrt);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.RetryAllFailedRequests();

        Assert.Equal(2, result.TotalFailed);
        Assert.Equal(0, result.Retried);
        Assert.Equal(2, result.BlockedByActiveRequest);
        Assert.Equal(2, result.RemainingFailed);

        var updatedFailedAss = await context.TranslationRequests.SingleAsync(tr => tr.Id == failedAss.Id);
        var updatedFailedSrt = await context.TranslationRequests.SingleAsync(tr => tr.Id == failedSrt.Id);

        Assert.Equal(TranslationStatus.Failed, updatedFailedAss.Status);
        Assert.Equal(TranslationStatus.Failed, updatedFailedSrt.Status);
        Assert.Null(updatedFailedSrt.IsActive);
        Assert.False(updatedFailedSrt.IsPriority);
    }

    [Fact]
    public async Task RetryAllFailedRequests_BlocksRetryWhenLegacyKeylessActiveRequestMatchesWorkload()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var workloadKey = $"library:{MediaType.Movie}:311";

        var legacyActiveAss = CreateRequest(1, 311, MediaType.Movie, "en", "pl", "/movies/movie.en.ass", TranslationStatus.Pending, now.AddMinutes(-5));
        legacyActiveAss.WorkloadItemKey = string.Empty;
        legacyActiveAss.SourceSubtitleFormat = ".ass";
        legacyActiveAss.RequiredOutputFormats = ".ass";
        legacyActiveAss.IsActive = true;

        var failedAss = CreateRequest(2, 311, MediaType.Movie, "en", "pl", "/movies/movie.en.ass", TranslationStatus.Failed, now.AddMinutes(-4));
        failedAss.WorkloadItemKey = workloadKey;
        failedAss.SourceSubtitleFormat = ".ass";
        failedAss.RequiredOutputFormats = ".ass";
        failedAss.IsActive = null;
        failedAss.FailedAt = now.AddMinutes(-4);
        failedAss.NextRetryAt = null;

        context.TranslationRequests.AddRange(legacyActiveAss, failedAss);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.RetryAllFailedRequests();

        Assert.Equal(1, result.TotalFailed);
        Assert.Equal(0, result.Retried);
        Assert.Equal(1, result.BlockedByActiveRequest);
        Assert.Equal(1, result.RemainingFailed);

        var updatedFailedAss = await context.TranslationRequests.SingleAsync(tr => tr.Id == failedAss.Id);
        Assert.Equal(TranslationStatus.Failed, updatedFailedAss.Status);
        Assert.Null(updatedFailedAss.IsActive);
    }

    [Fact]
    public async Task RetryTranslationRequest_BlocksLegacyCustomSourceWithStaleKeyWhenCanonicalActiveExists()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var activeCustom = new TranslationRequest
        {
            Id = 1,
            WorkloadKind = TranslationWorkloadKind.CustomSource,
            WorkloadItemKey = "custom:990",
            MediaId = null,
            CustomMediaItemId = 990,
            MediaType = MediaType.Movie,
            Title = "Active custom canonical",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = "/custom/item.en.srt",
            SourceSubtitleFormat = ".srt",
            RequiredOutputFormats = ".srt",
            Status = TranslationStatus.Pending,
            IsActive = true,
            CreatedAt = now.AddMinutes(-3),
            UpdatedAt = now.AddMinutes(-3)
        };

        var failedLegacyCustom = new TranslationRequest
        {
            Id = 2,
            WorkloadKind = TranslationWorkloadKind.Library,
            WorkloadItemKey = "library:Movie:0",
            MediaId = null,
            CustomMediaItemId = 990,
            MediaType = MediaType.Movie,
            Title = "Failed custom stale key",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = "/custom/item.en.srt",
            SourceSubtitleFormat = ".srt",
            RequiredOutputFormats = ".srt",
            Status = TranslationStatus.Failed,
            IsActive = null,
            CreatedAt = now.AddMinutes(-2),
            UpdatedAt = now.AddMinutes(-2),
            NextRetryAt = null
        };

        context.TranslationRequests.AddRange(activeCustom, failedLegacyCustom);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.RetryTranslationRequest(new TranslationRequest
        {
            Id = failedLegacyCustom.Id,
            Title = failedLegacyCustom.Title,
            SourceLanguage = failedLegacyCustom.SourceLanguage,
            TargetLanguage = failedLegacyCustom.TargetLanguage,
            MediaType = failedLegacyCustom.MediaType,
            Status = failedLegacyCustom.Status
        });

        Assert.NotNull(result);
        Assert.False(result!.Retried);
        Assert.True(result.BlockedByActiveRequest);

        var updatedRequest = await context.TranslationRequests.SingleAsync(tr => tr.Id == failedLegacyCustom.Id);
        Assert.Equal(TranslationStatus.Failed, updatedRequest.Status);
        Assert.Null(updatedRequest.IsActive);
        Assert.Equal(TranslationWorkloadKind.Library, updatedRequest.WorkloadKind);
        Assert.Equal("library:Movie:0", updatedRequest.WorkloadItemKey);
    }

    [Fact]
    public async Task RetryTranslationRequest_RetriesLegacyUploadWithStaleKeyAndNormalizesCanonicalKey()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var failedLegacyUpload = new TranslationRequest
        {
            Id = 1,
            WorkloadKind = TranslationWorkloadKind.Library,
            WorkloadItemKey = "library:Movie:0",
            MediaId = null,
            UploadBatchFileId = 991,
            MediaType = MediaType.Movie,
            Title = "Failed upload stale key",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = "/uploads/item.en.srt",
            SourceSubtitleFormat = ".srt",
            RequiredOutputFormats = ".srt",
            Status = TranslationStatus.Failed,
            IsActive = null,
            CreatedAt = now.AddMinutes(-2),
            UpdatedAt = now.AddMinutes(-2),
            NextRetryAt = now.AddHours(2)
        };

        context.TranslationRequests.Add(failedLegacyUpload);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.RetryTranslationRequest(new TranslationRequest
        {
            Id = failedLegacyUpload.Id,
            Title = failedLegacyUpload.Title,
            SourceLanguage = failedLegacyUpload.SourceLanguage,
            TargetLanguage = failedLegacyUpload.TargetLanguage,
            MediaType = failedLegacyUpload.MediaType,
            Status = failedLegacyUpload.Status
        });

        Assert.NotNull(result);
        Assert.True(result!.Retried);
        Assert.False(result.BlockedByActiveRequest);

        var updatedRequest = await context.TranslationRequests.SingleAsync(tr => tr.Id == failedLegacyUpload.Id);
        Assert.Equal(TranslationStatus.Pending, updatedRequest.Status);
        Assert.True(updatedRequest.IsActive);
        Assert.Null(updatedRequest.NextRetryAt);
        Assert.Equal(TranslationWorkloadKind.Upload, updatedRequest.WorkloadKind);
        Assert.Equal("upload:991", updatedRequest.WorkloadItemKey);
    }

    [Fact]
    public async Task RetryTranslationRequest_BlocksRetryWhenDifferentOutputFormatIsAlreadyActive()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var workloadKey = $"library:{MediaType.Movie}:320";

        var failedSrt = CreateRequest(1, 320, MediaType.Movie, "en", "pl", "/movies/movie.en.ass", TranslationStatus.Failed, now.AddMinutes(-2));
        failedSrt.WorkloadItemKey = workloadKey;
        failedSrt.SourceSubtitleFormat = ".ass";
        failedSrt.RequiredOutputFormats = ".srt";
        failedSrt.IsActive = null;
        failedSrt.FailedAt = now.AddMinutes(-2);

        var activeAss = CreateRequest(2, 320, MediaType.Movie, "en", "pl", "/movies/movie.en.ass", TranslationStatus.Pending, now.AddMinutes(-1));
        activeAss.WorkloadItemKey = workloadKey;
        activeAss.SourceSubtitleFormat = ".ass";
        activeAss.RequiredOutputFormats = ".ass";
        activeAss.IsActive = true;

        context.TranslationRequests.AddRange(failedSrt, activeAss);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.RetryTranslationRequest(new TranslationRequest
        {
            Id = failedSrt.Id,
            Title = failedSrt.Title,
            SourceLanguage = failedSrt.SourceLanguage,
            TargetLanguage = failedSrt.TargetLanguage,
            MediaType = failedSrt.MediaType,
            Status = failedSrt.Status
        });

        Assert.NotNull(result);
        Assert.False(result!.Retried);
        Assert.True(result.BlockedByActiveRequest);
        Assert.Contains("already active or pending", result.Message, StringComparison.OrdinalIgnoreCase);

        var updatedRequest = await context.TranslationRequests.SingleAsync(tr => tr.Id == failedSrt.Id);
        Assert.Equal(TranslationStatus.Failed, updatedRequest.Status);
        Assert.Null(updatedRequest.IsActive);
        Assert.False(updatedRequest.IsPriority);
    }

    [Theory]
    [InlineData("srt-only", ".srt")]
    [InlineData("both", ".ass,.srt")]
    public async Task RetryTranslationRequest_SuccessResponseClearsNextRetryAtAndNormalizesMetadata(
        string subtitleOutputMode,
        string expectedRequiredOutputFormats)
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var workloadKey = $"library:{MediaType.Movie}:321";

        var failedRequest = CreateRequest(
            1,
            321,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/movie.en.ass",
            TranslationStatus.Failed,
            now.AddMinutes(-2));
        failedRequest.WorkloadItemKey = workloadKey;
        failedRequest.SourceSubtitleFormat = ".ass";
        failedRequest.SubtitleOutputMode = subtitleOutputMode;
        failedRequest.RequiredOutputFormats = null;
        failedRequest.IsActive = null;
        failedRequest.FailedAt = now.AddMinutes(-2);
        failedRequest.NextRetryAt = now.AddHours(1);

        context.TranslationRequests.Add(failedRequest);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.RetryTranslationRequest(new TranslationRequest
        {
            Id = failedRequest.Id,
            Title = failedRequest.Title,
            SourceLanguage = failedRequest.SourceLanguage,
            TargetLanguage = failedRequest.TargetLanguage,
            MediaType = failedRequest.MediaType,
            Status = failedRequest.Status
        });

        Assert.NotNull(result);
        Assert.True(result!.Retried);
        Assert.False(result.BlockedByActiveRequest);
        Assert.Contains("restarted", result.Message, StringComparison.OrdinalIgnoreCase);

        var updatedRequest = await context.TranslationRequests.SingleAsync(tr => tr.Id == failedRequest.Id);
        Assert.Equal(expectedRequiredOutputFormats, updatedRequest.RequiredOutputFormats);
        Assert.Equal(TranslationStatus.Pending, updatedRequest.Status);
        Assert.True(updatedRequest.IsActive);
        Assert.Null(updatedRequest.NextRetryAt);
    }

    [Fact]
    public async Task CreateRequest_UsesUploadFileNameAsTitle_ForUploadWorkload()
    {
        await using var context = BuildContext();

        var batch = new UploadBatch
        {
            Id = 80,
            Name = "Upload batch",
            TargetLanguage = "pl",
            StoragePath = "/uploads/batch-80"
        };

        var uploadFile = new UploadBatchFile
        {
            Id = 81,
            UploadBatchId = batch.Id,
            UploadBatch = batch,
            FileKind = UploadBatchFileKind.Subtitle,
            Title = "Episode 01",
            OriginalFileName = "Episode.01.en.srt",
            StoredPath = "/uploads/batch-80/originals/Episode.01.en.srt",
            RelativeStoredPath = "originals/Episode.01.en.srt",
            FileSizeBytes = 1024,
            SelectedSourceLanguage = "en",
            Status = UploadBatchFileStatus.Ready
        };

        context.UploadBatches.Add(batch);
        context.UploadBatchFiles.Add(uploadFile);
        await context.SaveChangesAsync();

        var settingServiceMock = new Mock<ISettingService>();
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync("match-source");

        var service = CreateService(context, settingServiceMock: settingServiceMock);

        var requestId = await service.CreateRequest(new TranslateAbleSubtitle
        {
            MediaId = uploadFile.Id,
            WorkloadKind = TranslationWorkloadKind.Upload,
            UploadBatchFileId = uploadFile.Id,
            SubtitlePath = uploadFile.StoredPath,
            SubtitleFormat = ".srt",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaType = MediaType.Movie
        });

        var request = await context.TranslationRequests.SingleAsync(item => item.Id == requestId);
        Assert.Equal("Episode.01.en.srt", request.Title);
        Assert.Equal(TranslationWorkloadKind.Upload, request.WorkloadKind);
        Assert.Equal(uploadFile.Id, request.UploadBatchFileId);
    }

    private static LingarrDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LingarrDbContext(options);
    }

    private static LingarrDbContext BuildSqliteContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        var context = new LingarrDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private static TranslationRequest CreateRequest(
        int id,
        int mediaId,
        MediaType mediaType,
        string sourceLanguage,
        string targetLanguage,
        string subtitlePath,
        TranslationStatus status,
        DateTime createdAt)
    {
        return new TranslationRequest
        {
            Id = id,
            MediaId = mediaId,
            MediaType = mediaType,
            Title = $"Media {mediaId}",
            SourceLanguage = sourceLanguage,
            TargetLanguage = targetLanguage,
            SubtitleToTranslate = subtitlePath,
            Status = status,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }

    private static Show CreateShowWithEpisodes(
        int showId,
        string title,
        bool isPriority,
        DateTime now,
        params int[] episodeIds)
    {
        var show = new Show
        {
            Id = showId,
            SonarrId = showId,
            Title = title,
            Path = $"/shows/{showId}",
            DateAdded = now,
            IsPriority = isPriority,
            PriorityDate = isPriority ? now : null
        };

        var season = new Season
        {
            Id = showId,
            SeasonNumber = 1,
            Path = $"/shows/{showId}/season-1",
            Show = show
        };

        show.Seasons.Add(season);

        foreach (var episodeId in episodeIds)
        {
            season.Episodes.Add(new Episode
            {
                Id = episodeId,
                SonarrId = episodeId,
                EpisodeNumber = episodeId,
                Title = $"Episode {episodeId}",
                FileName = $"episode-{episodeId}.mkv",
                Path = $"/shows/{showId}/season-1/episode-{episodeId}.mkv",
                DateAdded = now,
                Season = season
            });
        }

        return show;
    }

    private static TranslationRequestService CreateService(
        LingarrDbContext context,
        Mock<ITranslationWorkerService>? workerServiceMock = null,
        Mock<ITranslationCancellationService>? cancellationServiceMock = null,
        Mock<IMediaStateService>? mediaStateServiceMock = null,
        Mock<ICustomMediaStateService>? customMediaStateServiceMock = null,
        Mock<ISettingService>? settingServiceMock = null)
    {
        workerServiceMock ??= new Mock<ITranslationWorkerService>();
        cancellationServiceMock ??= new Mock<ITranslationCancellationService>();
        mediaStateServiceMock ??= new Mock<IMediaStateService>();
        customMediaStateServiceMock ??= new Mock<ICustomMediaStateService>();
        settingServiceMock ??= new Mock<ISettingService>();

        var clientProxyMock = new Mock<IClientProxy>();
        clientProxyMock
            .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var hubClientsMock = new Mock<IHubClients>();
        hubClientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);

        var hubContextMock = new Mock<IHubContext<TranslationRequestsHub>>();
        hubContextMock.SetupGet(h => h.Clients).Returns(hubClientsMock.Object);

        return new TranslationRequestService(
            context,
            workerServiceMock.Object,
            hubContextMock.Object,
            new Mock<ITranslationServiceFactory>().Object,
            new Mock<IProgressService>().Object,
            new Mock<IStatisticsService>().Object,
            new Lazy<IMediaService>(() => new Mock<IMediaService>().Object),
            settingServiceMock.Object,
            new Mock<IBatchFallbackService>().Object,
            NullLogger<TranslationRequestService>.Instance,
            cancellationServiceMock.Object,
            mediaStateServiceMock.Object,
            customMediaStateServiceMock.Object);
    }
}
