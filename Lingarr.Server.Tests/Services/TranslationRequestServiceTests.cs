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
using Microsoft.AspNetCore.SignalR;
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
        var retried = await service.RetryAllFailedRequests();

        Assert.Equal(0, retried);

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
        var retried = await service.RetryAllFailedRequests();

        Assert.Equal(0, retried);

        var updatedFailedAss = await context.TranslationRequests.SingleAsync(tr => tr.Id == failedAss.Id);
        Assert.Equal(TranslationStatus.Failed, updatedFailedAss.Status);
        Assert.Null(updatedFailedAss.IsActive);
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
        var message = await service.RetryTranslationRequest(new TranslationRequest
        {
            Id = failedSrt.Id,
            Title = failedSrt.Title,
            SourceLanguage = failedSrt.SourceLanguage,
            TargetLanguage = failedSrt.TargetLanguage,
            MediaType = failedSrt.MediaType,
            Status = failedSrt.Status
        });

        Assert.NotNull(message);
        Assert.Contains("already active or pending", message!, StringComparison.OrdinalIgnoreCase);

        var updatedRequest = await context.TranslationRequests.SingleAsync(tr => tr.Id == failedSrt.Id);
        Assert.Equal(TranslationStatus.Failed, updatedRequest.Status);
        Assert.Null(updatedRequest.IsActive);
        Assert.False(updatedRequest.IsPriority);
    }

    [Theory]
    [InlineData("srt-only", ".srt")]
    [InlineData("both", ".ass,.srt")]
    public async Task RetryTranslationRequest_NormalizesLegacyRequiredOutputFormatsBeforeActivating(
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

        context.TranslationRequests.Add(failedRequest);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var message = await service.RetryTranslationRequest(new TranslationRequest
        {
            Id = failedRequest.Id,
            Title = failedRequest.Title,
            SourceLanguage = failedRequest.SourceLanguage,
            TargetLanguage = failedRequest.TargetLanguage,
            MediaType = failedRequest.MediaType,
            Status = failedRequest.Status
        });

        Assert.NotNull(message);
        Assert.Contains("restarted", message!, StringComparison.OrdinalIgnoreCase);

        var updatedRequest = await context.TranslationRequests.SingleAsync(tr => tr.Id == failedRequest.Id);
        Assert.Equal(expectedRequiredOutputFormats, updatedRequest.RequiredOutputFormats);
        Assert.Equal(TranslationStatus.Pending, updatedRequest.Status);
        Assert.True(updatedRequest.IsActive);
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
