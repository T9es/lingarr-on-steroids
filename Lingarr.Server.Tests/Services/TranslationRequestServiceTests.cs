using System;
using System.Collections.Generic;
using System.IO;
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
using Lingarr.Server.Models.Translation;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Subtitle;
using Lingarr.Server.Services.Translation;
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
    public async Task ReenqueueQueuedRequests_ClearsStaleOwnerAndPreservesCheckpoint()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            await using var context = BuildContext();
            var request = CreateRequest(
                1,
                12,
                MediaType.Movie,
                "en",
                "pl",
                "/movies/requeue-checkpoint.en.srt",
                TranslationStatus.Paused,
                DateTime.UtcNow);
            request.JobId = "stale-owner";
            request.StartedAt = DateTime.UtcNow.AddMinutes(-5);
            request.PausedAt = DateTime.UtcNow.AddMinutes(-1);
            context.TranslationRequests.Add(request);
            await context.SaveChangesAsync();

            var checkpointService = new TranslationCheckpointService(
                NullLogger<TranslationCheckpointService>.Instance,
                root);
            await checkpointService.SaveTranslationAsync(
                request.Id,
                "source-1",
                4,
                "preserve me",
                CancellationToken.None);

            var service = CreateService(
                context,
                translationCheckpointService: checkpointService);

            var result = await service.ReenqueueQueuedRequests();

            Assert.Equal(1, result.Reenqueued);
            var persistedRequest = await context.TranslationRequests
                .AsNoTracking()
                .SingleAsync(item => item.Id == request.Id);
            Assert.Equal(TranslationStatus.Pending, persistedRequest.Status);
            Assert.True(persistedRequest.IsActive);
            Assert.Null(persistedRequest.JobId);
            Assert.Null(persistedRequest.StartedAt);
            Assert.Null(persistedRequest.PausedAt);

            var checkpoint = await checkpointService.LoadByRequestIdAsync(
                request.Id,
                CancellationToken.None);
            Assert.NotNull(checkpoint);
            Assert.Equal("preserve me", checkpoint!.Translations[4]);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ReenqueueQueuedRequests_DoesNotDowngradeRequestThatChangedStateAfterRead()
    {
        await using var context = BuildContext();

        var request = CreateRequest(
            1,
            12,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/race.en.srt",
            TranslationStatus.Pending,
            DateTime.UtcNow);
        request.IsActive = true;
        context.TranslationRequests.Add(request);
        await context.SaveChangesAsync();

        var cancellationServiceMock = new Mock<ITranslationCancellationService>();
        using var replacementAttemptCts = new CancellationTokenSource();
        cancellationServiceMock
            .Setup(service => service.GetToken(request.Id))
            .Returns(replacementAttemptCts.Token);
        cancellationServiceMock
            .Setup(service => service.CancelJob(request.Id, replacementAttemptCts.Token))
            .Callback(() =>
            {
                var trackedRequest = context.TranslationRequests.Local.Single(item => item.Id == request.Id);
                trackedRequest.Status = TranslationStatus.InProgress;
                trackedRequest.IsActive = true;
                trackedRequest.JobId = "replacement-worker";
                context.SaveChanges();
            });

        var service = CreateService(
            context,
            cancellationServiceMock: cancellationServiceMock);

        var result = await service.ReenqueueQueuedRequests();

        Assert.Equal(0, result.Reenqueued);
        var persistedRequest = await context.TranslationRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == request.Id);
        Assert.Equal(TranslationStatus.InProgress, persistedRequest.Status);
    }

    [Fact]
    public async Task DedupeQueuedRequests_DoesNotDeleteDuplicateThatChangesStateAfterRead()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var canonical = CreateRequest(
            1,
            13,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/dedupe-race.en.srt",
            TranslationStatus.Pending,
            now);
        var duplicate = CreateRequest(
            2,
            13,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/dedupe-race.en.srt",
            TranslationStatus.Pending,
            now.AddSeconds(1));
        context.TranslationRequests.AddRange(canonical, duplicate);
        await context.SaveChangesAsync();

        var cancellationServiceMock = new Mock<ITranslationCancellationService>();
        cancellationServiceMock
            .Setup(service => service.GetToken(duplicate.Id))
            .Returns(CancellationToken.None)
            .Callback(() =>
            {
                var trackedRequest = context.TranslationRequests.Local.Single(item => item.Id == duplicate.Id);
                trackedRequest.Status = TranslationStatus.InProgress;
                trackedRequest.IsActive = true;
                trackedRequest.JobId = "replacement-worker";
                context.SaveChanges();
            });

        var service = CreateService(
            context,
            cancellationServiceMock: cancellationServiceMock);

        var (removed, skipped) = await service.DedupeQueuedRequests();

        Assert.Equal(0, removed);
        Assert.Equal(1, skipped);
        var persistedDuplicate = await context.TranslationRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == duplicate.Id);
        Assert.Equal(TranslationStatus.InProgress, persistedDuplicate.Status);
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
    public async Task GetTranslationRequests_DefaultQueueOrder_UsesCurrentMediaPriorityBeforeStaleRequestPriority()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var context = BuildSqliteContext(connection);

        var now = DateTime.UtcNow;
        var priorityShow = CreateShowWithEpisodes(130, "Priority Show", true, now, 231);
        var nonPriorityShow = CreateShowWithEpisodes(131, "Non Priority Show", false, now, 232);
        context.Shows.AddRange(priorityShow, nonPriorityShow);

        var priorityRequest = CreateRequest(
            1,
            231,
            MediaType.Episode,
            "en",
            "pl",
            "/shows/priority/s01e01.en.srt",
            TranslationStatus.Pending,
            now.AddMinutes(-10));
        priorityRequest.IsPriority = true;

        var stalePriorityRequest = CreateRequest(
            2,
            232,
            MediaType.Episode,
            "en",
            "pl",
            "/shows/non-priority/s01e01.en.srt",
            TranslationStatus.Pending,
            now);
        stalePriorityRequest.IsPriority = true;

        context.TranslationRequests.AddRange(priorityRequest, stalePriorityRequest);
        await context.SaveChangesAsync();
        await SetRequestCreatedAtAsync(context, priorityRequest.Id, now.AddMinutes(-10));
        await SetRequestCreatedAtAsync(context, stalePriorityRequest.Id, now);

        var service = CreateService(context);
        var result = await service.GetTranslationRequests(null, null, true, 1, 10);

        Assert.Equal(new[] { priorityRequest.Id, stalePriorityRequest.Id }, result.Items.Select(request => request.Id));
    }

    [Fact]
    public async Task GetTranslationRequests_QueueOrder_UsesNewestPriorityDateBeforeOlderPriorityDate()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var context = BuildSqliteContext(connection);

        var now = DateTime.UtcNow;
        var olderPriorityShow = CreateShowWithEpisodes(140, "Older Priority Show", true, now.AddDays(-2), 241);
        var newerPriorityShow = CreateShowWithEpisodes(141, "Newer Priority Show", true, now.AddDays(-1), 242);
        context.Shows.AddRange(olderPriorityShow, newerPriorityShow);

        var olderPriorityRequest = CreateRequest(
            1,
            241,
            MediaType.Episode,
            "en",
            "pl",
            "/shows/older/s01e01.en.srt",
            TranslationStatus.Pending,
            now);
        olderPriorityRequest.IsPriority = true;

        var newerPriorityRequest = CreateRequest(
            2,
            242,
            MediaType.Episode,
            "en",
            "pl",
            "/shows/newer/s01e01.en.srt",
            TranslationStatus.Pending,
            now.AddMinutes(-10));
        newerPriorityRequest.IsPriority = true;

        context.TranslationRequests.AddRange(olderPriorityRequest, newerPriorityRequest);
        await context.SaveChangesAsync();
        await SetRequestCreatedAtAsync(context, olderPriorityRequest.Id, now);
        await SetRequestCreatedAtAsync(context, newerPriorityRequest.Id, now.AddMinutes(-10));

        var service = CreateService(context);
        var result = await service.GetTranslationRequests(null, "Queue", true, 1, 10);

        Assert.Equal(new[] { newerPriorityRequest.Id, olderPriorityRequest.Id }, result.Items.Select(request => request.Id));
    }

    [Fact]
    public async Task GetTranslationRequests_QueueOrder_UsesCreatedAtWithinSamePriorityMedia()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var context = BuildSqliteContext(connection);

        var now = DateTime.UtcNow;
        var priorityShow = CreateShowWithEpisodes(150, "Priority Show", true, now, 251, 252);
        context.Shows.Add(priorityShow);

        var firstRequest = CreateRequest(
            1,
            251,
            MediaType.Episode,
            "en",
            "pl",
            "/shows/priority/s01e01.en.srt",
            TranslationStatus.Pending,
            now.AddMinutes(-10));
        firstRequest.IsPriority = true;

        var secondRequest = CreateRequest(
            2,
            252,
            MediaType.Episode,
            "en",
            "pl",
            "/shows/priority/s01e02.en.srt",
            TranslationStatus.Pending,
            now);
        secondRequest.IsPriority = true;

        context.TranslationRequests.AddRange(secondRequest, firstRequest);
        await context.SaveChangesAsync();
        await SetRequestCreatedAtAsync(context, firstRequest.Id, now.AddMinutes(-10));
        await SetRequestCreatedAtAsync(context, secondRequest.Id, now);

        var service = CreateService(context);
        var result = await service.GetTranslationRequests(null, "Queue", true, 1, 10);

        Assert.Equal(new[] { firstRequest.Id, secondRequest.Id }, result.Items.Select(request => request.Id));
    }

    [Fact]
    public async Task InterruptActiveRequestsForMedia_MarksRequestsInterruptedAndClearsMediaHash()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var context = BuildSqliteContext(connection);

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
        using var pendingAttemptCts = new CancellationTokenSource();
        using var inProgressAttemptCts = new CancellationTokenSource();
        cancellationMock
            .Setup(service => service.GetToken(1))
            .Returns(pendingAttemptCts.Token);
        cancellationMock
            .Setup(service => service.GetToken(2))
            .Returns(inProgressAttemptCts.Token);
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

        cancellationMock.Verify(c => c.CancelJob(1, pendingAttemptCts.Token), Times.Once);
        cancellationMock.Verify(c => c.CancelJob(2, inProgressAttemptCts.Token), Times.Once);
        mediaStateMock.Verify(m => m.UpdateStateAsync(It.IsAny<Movie>(), MediaType.Movie, true), Times.Once);
    }

    [Fact]
    public async Task InterruptActiveRequestsForMedia_HoldsAttemptOwnershipWhileDeletingCheckpoint()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var context = BuildSqliteContext(connection);

        var request = CreateRequest(
            1,
            51,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/checkpoint-interrupt.en.srt",
            TranslationStatus.InProgress,
            DateTime.UtcNow);
        request.JobId = "old-attempt";
        request.IsActive = true;
        context.TranslationRequests.Add(request);
        await context.SaveChangesAsync();

        using var attemptCts = new CancellationTokenSource();
        var cancellationServiceMock = new Mock<ITranslationCancellationService>();
        cancellationServiceMock
            .Setup(service => service.GetToken(request.Id, "old-attempt"))
            .Returns(attemptCts.Token);
        var checkpointServiceMock = new Mock<ITranslationCheckpointService>();
        checkpointServiceMock
            .Setup(service => service.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async (int requestId, CancellationToken _) =>
            {
                var currentRequest = await context.TranslationRequests
                    .AsNoTracking()
                    .SingleAsync(item => item.Id == requestId);

                Assert.Equal(TranslationStatus.InProgress, currentRequest.Status);
                Assert.StartsWith("interrupt:", currentRequest.JobId);
                Assert.Null(await TranslationWorkerService.TryClaimPendingRequestAsync(
                    context,
                    requestId,
                    CancellationToken.None));
            });

        var service = CreateService(
            context,
            cancellationServiceMock: cancellationServiceMock,
            translationCheckpointService: checkpointServiceMock.Object);

        var interrupted = await service.InterruptActiveRequestsForMedia(MediaType.Movie, 51);

        Assert.Equal(1, interrupted);
        var persistedRequest = await context.TranslationRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == request.Id);
        Assert.Equal(TranslationStatus.Interrupted, persistedRequest.Status);
        Assert.Null(persistedRequest.JobId);
        checkpointServiceMock.Verify(
            checkpoint => checkpoint.DeleteAsync(request.Id, It.IsAny<CancellationToken>()),
            Times.Once);
        cancellationServiceMock.Verify(
            cancellation => cancellation.CancelJob(request.Id, attemptCts.Token),
            Times.Once);
    }

    [Fact]
    public async Task InterruptActiveRequestsForMedia_IgnoresUploadRequestsWithCollidingMediaId()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var context = BuildSqliteContext(connection);

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

    [Theory]
    [InlineData(TranslationStatus.Failed)]
    [InlineData(TranslationStatus.Completed)]
    [InlineData(TranslationStatus.Interrupted)]
    [InlineData(TranslationStatus.Cancelled)]
    public async Task CancelTranslationRequest_DoesNotDowngradeTerminalRequest(
        TranslationStatus terminalStatus)
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var context = BuildSqliteContext(connection);

        var request = CreateRequest(
            1,
            56,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/movie.en.srt",
            terminalStatus,
            DateTime.UtcNow);
        request.IsActive = null;
        context.TranslationRequests.Add(request);
        await context.SaveChangesAsync();

        var cancellationMock = new Mock<ITranslationCancellationService>();
        var mediaStateMock = new Mock<IMediaStateService>();
        var service = CreateService(
            context,
            cancellationServiceMock: cancellationMock,
            mediaStateServiceMock: mediaStateMock);

        var result = await service.CancelTranslationRequest(new TranslationRequest
        {
            Id = request.Id,
            Title = request.Title,
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            MediaType = request.MediaType,
            Status = request.Status
        });

        Assert.NotNull(result);
        var persistedRequest = await context.TranslationRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == request.Id);
        Assert.Equal(terminalStatus, persistedRequest.Status);
        Assert.Null(persistedRequest.IsActive);
        cancellationMock.Verify(service => service.CancelJob(request.Id), Times.Never);
        mediaStateMock.Verify(
            service => service.UpdateStateAsync(
                It.IsAny<Lingarr.Core.Interfaces.IMedia>(),
                It.IsAny<MediaType>(),
                It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task CancelAllQueuedRequests_DoesNotCancelRequestReclaimedAfterRead()
    {
        await using var context = BuildContext();

        var request = CreateRequest(
            1,
            57,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/cancel-all-race.en.srt",
            TranslationStatus.Pending,
            DateTime.UtcNow);
        context.TranslationRequests.Add(request);
        await context.SaveChangesAsync();

        var cancellationServiceMock = new Mock<ITranslationCancellationService>();
        cancellationServiceMock
            .Setup(service => service.GetToken(request.Id))
            .Returns(CancellationToken.None)
            .Callback(() =>
            {
                var trackedRequest = context.TranslationRequests.Local.Single(item => item.Id == request.Id);
                trackedRequest.Status = TranslationStatus.InProgress;
                trackedRequest.IsActive = true;
                trackedRequest.JobId = "replacement-worker";
                context.SaveChanges();
            });

        var service = CreateService(
            context,
            cancellationServiceMock: cancellationServiceMock);

        var result = await service.CancelAllQueuedRequests(includeInProgress: true);

        Assert.Equal(0, result.Cancelled);
        Assert.Equal(1, result.SkippedProcessing);
        var persistedRequest = await context.TranslationRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == request.Id);
        Assert.Equal(TranslationStatus.InProgress, persistedRequest.Status);
        Assert.Equal("replacement-worker", persistedRequest.JobId);
        cancellationServiceMock.Verify(
            service => service.CancelJob(It.IsAny<int>()),
            Times.Never);
        cancellationServiceMock.Verify(
            service => service.CancelJob(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(TranslationStatus.Failed)]
    [InlineData(TranslationStatus.Interrupted)]
    [InlineData(TranslationStatus.Cancelled)]
    public async Task InterruptActiveRequestsForMedia_IgnoresTerminalRequest(
        TranslationStatus terminalStatus)
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var context = BuildSqliteContext(connection);

        var movie = new Movie
        {
            Id = 56,
            RadarrId = 56,
            Title = "Movie 56",
            FileName = "movie-56.mkv",
            Path = "/movies",
            MediaHash = "hash",
            DateAdded = DateTime.UtcNow
        };
        var request = CreateRequest(
            1,
            movie.Id,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/movie-56.en.srt",
            terminalStatus,
            DateTime.UtcNow);
        request.IsActive = null;

        context.Movies.Add(movie);
        context.TranslationRequests.Add(request);
        await context.SaveChangesAsync();

        var cancellationMock = new Mock<ITranslationCancellationService>();
        var mediaStateMock = new Mock<IMediaStateService>();
        var service = CreateService(
            context,
            cancellationServiceMock: cancellationMock,
            mediaStateServiceMock: mediaStateMock);

        var interrupted = await service.InterruptActiveRequestsForMedia(MediaType.Movie, movie.Id);

        Assert.Equal(0, interrupted);
        var persistedRequest = await context.TranslationRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == request.Id);
        Assert.Equal(terminalStatus, persistedRequest.Status);
        Assert.Null(persistedRequest.IsActive);
        Assert.Equal("hash", (await context.Movies.AsNoTracking().SingleAsync(item => item.Id == movie.Id)).MediaHash);
        cancellationMock.Verify(service => service.CancelJob(request.Id), Times.Never);
        mediaStateMock.Verify(
            service => service.UpdateStateAsync(
                It.IsAny<Lingarr.Core.Interfaces.IMedia>(),
                It.IsAny<MediaType>(),
                It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateTranslationRequest_DoesNotStartInterruptedRequest()
    {
        await using var context = BuildContext();
        var request = CreateRequest(
            1,
            57,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/movie-57.en.srt",
            TranslationStatus.Interrupted,
            DateTime.UtcNow);
        request.IsActive = null;
        context.TranslationRequests.Add(request);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            service.UpdateTranslationRequest(request, TranslationStatus.InProgress));

        var persistedRequest = await context.TranslationRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == request.Id);
        Assert.Equal(TranslationStatus.Interrupted, persistedRequest.Status);
        Assert.Null(persistedRequest.IsActive);
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

    [Fact]
    public async Task CreateRequest_ReturnsExistingCompletedNonSupplementalRequestInsteadOfDuplicate()
    {
        await using var context = BuildContext();

        var service = CreateService(context);
        var createdAt = DateTime.UtcNow;
        var workloadKey = $"library:{MediaType.Movie}:63";
        var firstRequest = CreateRequest(
            1,
            63,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/movie.en.srt",
            TranslationStatus.Pending,
            createdAt);
        firstRequest.WorkloadItemKey = workloadKey;

        var firstId = await service.CreateRequest(firstRequest);
        var existing = await context.TranslationRequests.SingleAsync();
        existing.Status = TranslationStatus.Completed;
        existing.IsActive = false;
        existing.CompletedAt = createdAt.AddMinutes(1);
        await context.SaveChangesAsync();

        var duplicateRequest = CreateRequest(
            2,
            63,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/movie.en.srt",
            TranslationStatus.Pending,
            createdAt.AddMinutes(2));
        duplicateRequest.WorkloadItemKey = workloadKey;

        var duplicateId = await service.CreateRequest(duplicateRequest);

        Assert.Equal(firstId, duplicateId);
        Assert.Equal(1, await context.TranslationRequests.CountAsync());
        Assert.Equal(TranslationStatus.Completed, existing.Status);
        Assert.False(existing.IsActive);
    }

    [Fact]
    public async Task CreateRequest_ForcedDialogueWithForcedFlagDedupesAsPrimary()
    {
        await using var context = BuildContext();

        var service = CreateService(context);
        var createdAt = DateTime.UtcNow;
        var workloadKey = $"library:{MediaType.Movie}:163";
        var firstRequest = CreateRequest(
            1,
            163,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/movie.en.srt",
            TranslationStatus.Pending,
            createdAt);
        firstRequest.WorkloadItemKey = workloadKey;
        firstRequest.SourceSubtitleType = SubtitleLanguageHelper.TypeForcedDialogue;
        firstRequest.IsForcedSubtitle = true;
        firstRequest.SourceSnapshotIdentity = "embedded|en|stream:0";
        firstRequest.SourceSnapshotStreamIndex = 0;

        var firstId = await service.CreateRequest(firstRequest);
        var existing = await context.TranslationRequests.SingleAsync();
        existing.Status = TranslationStatus.Completed;
        existing.IsActive = false;
        existing.CompletedAt = createdAt.AddMinutes(1);
        await context.SaveChangesAsync();

        var duplicateRequest = CreateRequest(
            2,
            163,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/movie.en.srt",
            TranslationStatus.Pending,
            createdAt.AddMinutes(2));
        duplicateRequest.WorkloadItemKey = workloadKey;
        duplicateRequest.SourceSubtitleType = SubtitleLanguageHelper.TypeForcedDialogue;
        duplicateRequest.IsForcedSubtitle = true;
        duplicateRequest.SourceSnapshotIdentity = "embedded|en|stream:0";
        duplicateRequest.SourceSnapshotStreamIndex = 0;

        var duplicateId = await service.CreateRequest(duplicateRequest);

        Assert.Equal(firstId, duplicateId);
        Assert.Equal("primary", existing.SourceDedupeKey);
        Assert.Equal(1, await context.TranslationRequests.CountAsync());
    }

    [Fact]
    public async Task CreateRequest_ForcePriorityRestartsExistingNonSupplementalRequest()
    {
        await using var context = BuildContext();

        var service = CreateService(context);
        var createdAt = DateTime.UtcNow;
        var workloadKey = $"library:{MediaType.Movie}:64";
        var firstRequest = CreateRequest(
            1,
            64,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/movie.en.srt",
            TranslationStatus.Pending,
            createdAt);
        firstRequest.WorkloadItemKey = workloadKey;

        var firstId = await service.CreateRequest(firstRequest);
        var existing = await context.TranslationRequests.SingleAsync();
        existing.Status = TranslationStatus.Completed;
        existing.IsActive = false;
        existing.CompletedAt = createdAt.AddMinutes(1);
        existing.Progress = 100;
        await context.SaveChangesAsync();

        var forcedRequest = CreateRequest(
            2,
            64,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/movie.en.srt",
            TranslationStatus.Pending,
            createdAt.AddMinutes(2));
        forcedRequest.WorkloadItemKey = workloadKey;
        forcedRequest.SourceSubtitleEntryCount = 406;
        forcedRequest.SelectedStreamTitle = "English (SDH)";

        var forcedId = await service.CreateRequest(forcedRequest, forcePriority: true);

        Assert.Equal(firstId, forcedId);
        Assert.Equal(1, await context.TranslationRequests.CountAsync());
        Assert.Equal(TranslationStatus.Pending, existing.Status);
        Assert.True(existing.IsActive);
        Assert.True(existing.IsPriority);
        Assert.Null(existing.CompletedAt);
        Assert.Equal(0, existing.Progress);
        Assert.Equal(406, existing.SourceSubtitleEntryCount);
        Assert.Equal("English (SDH)", existing.SelectedStreamTitle);
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
    public async Task RetryAllFailedRequests_PreservesHealthyCheckpointTranslations()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            await using var context = BuildContext();
            var failedRequest = CreateRequest(
                1,
                314,
                MediaType.Movie,
                "en",
                "pl",
                "/movies/checkpoint-bulk.en.srt",
                TranslationStatus.Failed,
                DateTime.UtcNow.AddMinutes(-1));
            failedRequest.IsActive = null;
            context.TranslationRequests.Add(failedRequest);
            await context.SaveChangesAsync();

            var checkpointService = new TranslationCheckpointService(
                NullLogger<TranslationCheckpointService>.Instance,
                root);
            await checkpointService.SaveTranslationAsync(
                failedRequest.Id,
                "source-1",
                7,
                "healthy translation",
                CancellationToken.None);

            var service = CreateService(
                context,
                translationCheckpointService: checkpointService);

            var result = await service.RetryAllFailedRequests();

            Assert.Equal(1, result.Retried);
            var checkpoint = await checkpointService.LoadAsync(
                failedRequest.Id,
                "source-1",
                CancellationToken.None);
            Assert.NotNull(checkpoint);
            Assert.Equal("healthy translation", checkpoint.Translations[7]);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RetryAllFailedRequests_RetriesInterruptedRequestsAndLeavesCancelledRequestsOut()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var failedRequest = CreateRequest(
            1,
            301,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/failed-retry.en.srt",
            TranslationStatus.Failed,
            now.AddMinutes(-3));
        var interruptedRequest = CreateRequest(
            2,
            302,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/interrupted-retry.en.srt",
            TranslationStatus.Interrupted,
            now.AddMinutes(-2));
        var cancelledRequest = CreateRequest(
            3,
            303,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/cancelled-retry.en.srt",
            TranslationStatus.Cancelled,
            now.AddMinutes(-1));

        context.TranslationRequests.AddRange(failedRequest, interruptedRequest, cancelledRequest);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.RetryAllFailedRequests();

        Assert.Equal(2, result.TotalFailed);
        Assert.Equal(2, result.Retried);
        Assert.Equal(0, result.RemainingFailed);

        var persistedRequests = await context.TranslationRequests
            .AsNoTracking()
            .OrderBy(request => request.Id)
            .ToListAsync();
        Assert.Equal(TranslationStatus.Pending, persistedRequests[0].Status);
        Assert.Equal(TranslationStatus.Pending, persistedRequests[1].Status);
        Assert.Equal(TranslationStatus.Cancelled, persistedRequests[2].Status);
        Assert.True(persistedRequests[0].IsActive);
        Assert.True(persistedRequests[1].IsActive);
    }

    [Theory]
    [InlineData(TranslationStatus.InProgress)]
    [InlineData(TranslationStatus.Cancelled)]
    public async Task RetryAllFailedRequests_DoesNotOverwriteRequestThatChangesStateAfterRead(
        TranslationStatus newerStatus)
    {
        await using var context = BuildContext();

        var failedRequest = CreateRequest(
            1,
            317,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/retry-race.en.srt",
            TranslationStatus.Failed,
            DateTime.UtcNow.AddMinutes(-1));
        failedRequest.IsActive = null;
        context.TranslationRequests.Add(failedRequest);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        service.BeforeFailedRequestRetryAsync = requestId =>
        {
            var trackedRequest = context.TranslationRequests.Local.Single(item => item.Id == requestId);
            trackedRequest.Status = newerStatus;
            trackedRequest.IsActive = newerStatus == TranslationStatus.InProgress ? true : null;
            trackedRequest.CompletedAt = DateTime.UtcNow;
            context.SaveChanges();
            return Task.CompletedTask;
        };

        var result = await service.RetryAllFailedRequests();

        Assert.Equal(1, result.TotalFailed);
        Assert.Equal(0, result.Retried);

        var persistedRequest = await context.TranslationRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == failedRequest.Id);
        Assert.Equal(newerStatus, persistedRequest.Status);
        var expectedIsActive = newerStatus == TranslationStatus.InProgress ? (bool?)true : null;
        Assert.Equal(expectedIsActive, persistedRequest.IsActive);
    }

    [Fact]
    public async Task RetryAllFailedRequests_PermanentResolutionDeletesCheckpointAfterRowDelete()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            await using var context = BuildContext();
            var failedRequest = CreateRequest(
                1,
                317,
                MediaType.Movie,
                "en",
                "pl",
                "/movies/permanent-resolution.en.srt",
                TranslationStatus.Failed,
                DateTime.UtcNow);
            failedRequest.RetryCount = 1;
            context.TranslationRequests.Add(failedRequest);
            await context.SaveChangesAsync();

            var checkpointService = new TranslationCheckpointService(
                NullLogger<TranslationCheckpointService>.Instance,
                root);
            await checkpointService.SaveTranslationAsync(
                failedRequest.Id,
                "source-1",
                2,
                "partial translation",
                CancellationToken.None);

            var settingServiceMock = new Mock<ISettingService>();
            settingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.Translation.MaxRequestRetries))
                .ReturnsAsync("1");
            var service = CreateService(
                context,
                settingServiceMock: settingServiceMock,
                translationCheckpointService: checkpointService);

            var result = await service.RetryAllFailedRequests();

            Assert.Equal(1, result.TotalFailed);
            Assert.Equal(0, result.Retried);
            Assert.Equal(0, result.RemainingFailed);
            Assert.False(await context.TranslationRequests.AnyAsync());
            Assert.Null(await checkpointService.LoadByRequestIdAsync(
                failedRequest.Id,
                CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RemoveAllFailedRequests_RemovesInterruptedRequestsAndLeavesCancelledRequestsOut()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var failedRequest = CreateRequest(
            1,
            304,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/failed-remove.en.srt",
            TranslationStatus.Failed,
            now.AddMinutes(-3));
        var interruptedRequest = CreateRequest(
            2,
            305,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/interrupted-remove.en.srt",
            TranslationStatus.Interrupted,
            now.AddMinutes(-2));
        var cancelledRequest = CreateRequest(
            3,
            306,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/cancelled-remove.en.srt",
            TranslationStatus.Cancelled,
            now.AddMinutes(-1));

        context.TranslationRequests.AddRange(failedRequest, interruptedRequest, cancelledRequest);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var removed = await service.RemoveAllFailedRequests();

        Assert.Equal(2, removed);
        var remainingRequests = await context.TranslationRequests
            .AsNoTracking()
            .ToListAsync();
        var remainingRequest = Assert.Single(remainingRequests);
        Assert.Equal(cancelledRequest.Id, remainingRequest.Id);
        Assert.Equal(TranslationStatus.Cancelled, remainingRequest.Status);
    }

    [Fact]
    public async Task RemoveAllFailedRequests_DoesNotDeleteRequestThatChangesStateAfterRead()
    {
        await using var context = BuildContext();

        var failedRequest = CreateRequest(
            1,
            315,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/remove-race.en.srt",
            TranslationStatus.Failed,
            DateTime.UtcNow);
        failedRequest.IsActive = null;
        context.TranslationRequests.Add(failedRequest);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        service.BeforeFailedRequestDeletionAsync = requestId =>
        {
            var trackedRequest = context.TranslationRequests.Local.Single(item => item.Id == requestId);
            trackedRequest.Status = TranslationStatus.Pending;
            trackedRequest.IsActive = true;
            context.SaveChanges();
            return Task.CompletedTask;
        };

        var removed = await service.RemoveAllFailedRequests();

        Assert.Equal(0, removed);
        var persistedRequest = await context.TranslationRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == failedRequest.Id);
        Assert.Equal(TranslationStatus.Pending, persistedRequest.Status);
    }

    [Fact]
    public async Task GetFailedRequests_PopulatesLatestFailureMessageFromNewestErrorOrWarningLog()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var failedWithDetails = CreateRequest(
            1,
            100,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/failed-details.en.srt",
            TranslationStatus.Failed,
            now);
        failedWithDetails.CompletedAt = now.AddMinutes(-1);

        var failedWithMessage = CreateRequest(
            2,
            101,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/failed-message.en.srt",
            TranslationStatus.Failed,
            now.AddSeconds(1));
        failedWithMessage.CompletedAt = now.AddMinutes(-2);

        var interruptedRequest = CreateRequest(
            3,
            102,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/interrupted.en.srt",
            TranslationStatus.Interrupted,
            now.AddSeconds(2));
        interruptedRequest.CompletedAt = now.AddMinutes(-3);

        var pendingRequest = CreateRequest(
            4,
            103,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/pending.en.srt",
            TranslationStatus.Pending,
            now.AddSeconds(3));

        context.TranslationRequests.AddRange(
            failedWithDetails,
            failedWithMessage,
            interruptedRequest,
            pendingRequest);
        context.TranslationRequestLogs.AddRange(
            new TranslationRequestLog
            {
                Id = 1,
                TranslationRequestId = failedWithDetails.Id,
                Level = "Error",
                Message = "Older error",
                Details = "Older details",
                CreatedAt = now.AddMinutes(-4)
            },
            new TranslationRequestLog
            {
                Id = 2,
                TranslationRequestId = failedWithDetails.Id,
                Level = "Warning",
                Message = "Latest warning",
                Details = "Preferred details",
                CreatedAt = now.AddMinutes(-3)
            },
            new TranslationRequestLog
            {
                Id = 3,
                TranslationRequestId = failedWithDetails.Id,
                Level = "Information",
                Message = "Newest informational message",
                Details = "Must be ignored",
                CreatedAt = now.AddMinutes(-2)
            },
            new TranslationRequestLog
            {
                Id = 4,
                TranslationRequestId = failedWithMessage.Id,
                Level = "Error",
                Message = "Fallback message",
                Details = "   ",
                CreatedAt = now.AddMinutes(-3)
            },
            new TranslationRequestLog
            {
                Id = 5,
                TranslationRequestId = pendingRequest.Id,
                Level = "Error",
                Message = "Pending request error",
                Details = "Must not be exposed",
                CreatedAt = now.AddMinutes(-1)
            },
            new TranslationRequestLog
            {
                Id = 6,
                TranslationRequestId = interruptedRequest.Id,
                Level = "Warning",
                Message = "Interrupted request warning",
                Details = "Interrupted failure details",
                CreatedAt = now.AddMinutes(-4)
            });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var requests = await service.GetFailedRequests();

        Assert.Equal(
            [failedWithDetails.Id, failedWithMessage.Id, interruptedRequest.Id],
            requests.Select(request => request.Id));
        Assert.Equal("Preferred details", requests[0].LatestFailureMessage);
        Assert.Equal("Fallback message", requests[1].LatestFailureMessage);
        Assert.Equal("Interrupted failure details", requests[2].LatestFailureMessage);
    }

    [Fact]
    public async Task GetOverview_IncludesInterruptedRequestsAndExcludesCancelledRequests()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var failedRequest = CreateRequest(
            1,
            104,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/failed-overview.en.srt",
            TranslationStatus.Failed,
            now);
        failedRequest.CompletedAt = now.AddMinutes(-1);

        var interruptedRequest = CreateRequest(
            2,
            105,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/interrupted-overview.en.srt",
            TranslationStatus.Interrupted,
            now.AddSeconds(1));
        interruptedRequest.CompletedAt = now.AddMinutes(-2);

        var cancelledRequest = CreateRequest(
            3,
            106,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/cancelled-overview.en.srt",
            TranslationStatus.Cancelled,
            now.AddSeconds(2));
        cancelledRequest.CompletedAt = now.AddMinutes(-3);

        context.TranslationRequests.AddRange(failedRequest, interruptedRequest, cancelledRequest);
        context.TranslationRequestLogs.AddRange(
            new TranslationRequestLog
            {
                TranslationRequestId = failedRequest.Id,
                Level = "Error",
                Message = "Failed overview message",
                Details = "Failed overview details",
                CreatedAt = now.AddMinutes(-3)
            },
            new TranslationRequestLog
            {
                TranslationRequestId = interruptedRequest.Id,
                Level = "Error",
                Message = "Interrupted overview message",
                Details = "Interrupted overview details",
                CreatedAt = now.AddMinutes(-4)
            });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var overview = await service.GetOverview(null, "CreatedAt", true, 1, 20, 10);

        Assert.Equal(2, overview.Failed.TotalCount);
        Assert.Equal(
            [failedRequest.Id, interruptedRequest.Id],
            overview.Failed.Items.Select(request => request.Id));
        Assert.Equal("Failed overview details", overview.Failed.Items[0].LatestFailureMessage);
        Assert.Equal("Interrupted overview details", overview.Failed.Items[1].LatestFailureMessage);
        Assert.DoesNotContain(overview.Failed.Items, request => request.Status == TranslationStatus.Cancelled);
    }

    [Fact]
    public async Task GetOverview_ReturnsCountsWithAllFailedAndBoundedInProgressItems()
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
        context.TranslationRequestLogs.AddRange(
            new TranslationRequestLog
            {
                TranslationRequestId = requests[2].Id,
                Level = "Warning",
                Message = "Older warning",
                Details = "Older details",
                CreatedAt = now.AddMinutes(-3)
            },
            new TranslationRequestLog
            {
                TranslationRequestId = requests[2].Id,
                Level = "Error",
                Message = "Latest failure",
                Details = "Overview failure details",
                CreatedAt = now.AddMinutes(-2)
            },
            new TranslationRequestLog
            {
                TranslationRequestId = requests[2].Id,
                Level = "Information",
                Message = "Newest informational message",
                Details = "Must be ignored",
                CreatedAt = now.AddMinutes(-1)
            },
            new TranslationRequestLog
            {
                TranslationRequestId = requests[0].Id,
                Level = "Error",
                Message = "Pending request error",
                Details = "Must not be exposed",
                CreatedAt = now
            });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var overview = await service.GetOverview(null, "CreatedAt", true, 1, 20, 2);

        Assert.Equal(4, overview.ActiveCount);
        Assert.Equal(2, overview.Pending.TotalCount);
        Assert.Equal(2, overview.Pending.Items.Count());
        Assert.Equal(3, overview.Failed.TotalCount);
        Assert.Equal(3, overview.Failed.Items.Count);
        Assert.Equal(
            [requests[2].Id, requests[3].Id, requests[4].Id],
            overview.Failed.Items.Select(request => request.Id));
        Assert.Equal("Overview failure details", overview.Failed.Items[0].LatestFailureMessage);
        Assert.Equal(2, overview.InProgress.TotalCount);
        Assert.Equal(2, overview.InProgress.Items.Count);
        Assert.Null(overview.Pending.Items.Single(request => request.Id == requests[0].Id).LatestFailureMessage);
        Assert.DoesNotContain(overview.Failed.Items, request => request.Status != TranslationStatus.Failed);
        Assert.DoesNotContain(overview.InProgress.Items, request =>
            request.Status != TranslationStatus.InProgress && request.Status != TranslationStatus.Paused);
    }

    [Fact]
    public async Task GetOverview_ReturnsEveryFailedRequestWhenThereAreMoreThanTheLegacySectionLimit()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var failedRequests = Enumerable.Range(1, 101)
            .Select(index => CreateRequest(
                index,
                500 + index,
                MediaType.Movie,
                "en",
                "pl",
                $"/movies/failed-{index}.en.srt",
                TranslationStatus.Failed,
                now.AddSeconds(index)))
            .ToList();
        for (var index = 0; index < failedRequests.Count; index++)
        {
            failedRequests[index].CompletedAt = now.AddSeconds(index + 1);
        }
        context.TranslationRequests.AddRange(failedRequests);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var overview = await service.GetOverview(null, null, true, 1, 20, 1);

        Assert.Equal(101, overview.Failed.TotalCount);
        Assert.Equal(101, overview.Failed.Items.Count);
        Assert.Equal(
            failedRequests
                .OrderByDescending(request => request.CompletedAt)
                .Select(request => request.Id),
            overview.Failed.Items.Select(request => request.Id));
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
    public async Task RetryTranslationRequest_PreservesHealthyCheckpointTranslations()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            await using var context = BuildContext();
            var failedRequest = CreateRequest(
                1,
                316,
                MediaType.Movie,
                "en",
                "pl",
                "/movies/checkpoint-individual.en.srt",
                TranslationStatus.Failed,
                DateTime.UtcNow.AddMinutes(-1));
            failedRequest.IsActive = null;
            context.TranslationRequests.Add(failedRequest);
            await context.SaveChangesAsync();

            var checkpointService = new TranslationCheckpointService(
                NullLogger<TranslationCheckpointService>.Instance,
                root);
            await checkpointService.SaveTranslationAsync(
                failedRequest.Id,
                "source-1",
                9,
                "healthy individual translation",
                CancellationToken.None);

            var service = CreateService(
                context,
                translationCheckpointService: checkpointService);

            var result = await service.RetryTranslationRequest(failedRequest);

            Assert.NotNull(result);
            Assert.True(result!.Retried);
            var checkpoint = await checkpointService.LoadAsync(
                failedRequest.Id,
                "source-1",
                CancellationToken.None);
            Assert.NotNull(checkpoint);
            Assert.Equal("healthy individual translation", checkpoint.Translations[9]);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(TranslationStatus.InProgress)]
    [InlineData(TranslationStatus.Completed)]
    [InlineData(TranslationStatus.Cancelled)]
    public async Task RetryTranslationRequest_DoesNotOverwriteRequestThatChangesStateAfterRead(
        TranslationStatus newerStatus)
    {
        await using var context = BuildContext();

        var failedRequest = CreateRequest(
            1,
            318,
            MediaType.Movie,
            "en",
            "pl",
            "/movies/retry-individual-race.en.srt",
            TranslationStatus.Failed,
            DateTime.UtcNow.AddMinutes(-1));
        failedRequest.IsActive = null;
        context.TranslationRequests.Add(failedRequest);
        await context.SaveChangesAsync();

        var workerServiceMock = new Mock<ITranslationWorkerService>();
        var service = CreateService(context, workerServiceMock: workerServiceMock);
        service.BeforeFailedRequestRetryAsync = requestId =>
        {
            var trackedRequest = context.TranslationRequests.Local.Single(item => item.Id == requestId);
            trackedRequest.Status = newerStatus;
            trackedRequest.IsActive = newerStatus == TranslationStatus.InProgress ? true : null;
            trackedRequest.CompletedAt = DateTime.UtcNow;
            context.SaveChanges();
            return Task.CompletedTask;
        };

        var result = await service.RetryTranslationRequest(failedRequest);

        Assert.NotNull(result);
        Assert.False(result!.Retried);
        Assert.False(result.BlockedByActiveRequest);
        Assert.Contains("state changed", result.Message, StringComparison.OrdinalIgnoreCase);
        workerServiceMock.Verify(worker => worker.Signal(), Times.Never);

        var persistedRequest = await context.TranslationRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == failedRequest.Id);
        Assert.Equal(newerStatus, persistedRequest.Status);
        var expectedIsActive = newerStatus == TranslationStatus.InProgress ? (bool?)true : null;
        Assert.Equal(expectedIsActive, persistedRequest.IsActive);
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

    private static Task SetRequestCreatedAtAsync(
        LingarrDbContext context,
        int requestId,
        DateTime createdAt)
    {
        context.ChangeTracker.Clear();
        return context.TranslationRequests
            .Where(request => request.Id == requestId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(request => request.CreatedAt, createdAt)
                .SetProperty(request => request.UpdatedAt, createdAt));
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
        Mock<ISettingService>? settingServiceMock = null,
        ITranslationCheckpointService? translationCheckpointService = null)
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
            customMediaStateServiceMock.Object,
            translationCheckpointService);
    }
}
