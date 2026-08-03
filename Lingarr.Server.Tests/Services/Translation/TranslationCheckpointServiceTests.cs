using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Models.Translation;
using Lingarr.Server.Services.Translation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class TranslationCheckpointServiceTests
{
    [Fact]
    public async Task SaveAndLoadAsync_PreservesTranslationsForMatchingSourceFingerprint()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new TranslationCheckpointService(
            NullLogger<TranslationCheckpointService>.Instance,
            root);

        await service.SaveTranslationAsync(42, "source-1", 7, "translated line", CancellationToken.None);

        var checkpoint = await service.LoadAsync(42, "source-1", CancellationToken.None);

        Assert.NotNull(checkpoint);
        Assert.True(checkpoint.Translations.TryGetValue(7, out var translated));
        Assert.Equal("translated line", translated);
    }

    [Fact]
    public async Task SaveAndLoadAsync_PreservesSourcePreservedPositions()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new TranslationCheckpointService(
            NullLogger<TranslationCheckpointService>.Instance,
            root);

        try
        {
            await service.SaveCheckpointAsync(
                new TranslationCheckpoint
                {
                    TranslationRequestId = 43,
                    SourceFingerprint = "source-1",
                    Translations = new Dictionary<int, string>
                    {
                        [7] = "opening source text"
                    },
                    SourcePreservedPositions = [7]
                },
                CancellationToken.None);

            var checkpoint = await service.LoadAsync(43, "source-1", CancellationToken.None);

            Assert.NotNull(checkpoint);
            Assert.Equal([7], checkpoint!.SourcePreservedPositions.Order());
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
    public async Task LoadAsync_ReturnsNullWhenSourceFingerprintDiffers()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new TranslationCheckpointService(
            NullLogger<TranslationCheckpointService>.Instance,
            root);

        await service.SaveTranslationAsync(42, "source-1", 7, "translated line", CancellationToken.None);

        var checkpoint = await service.LoadAsync(42, "source-2", CancellationToken.None);

        Assert.Null(checkpoint);
    }

    [Fact]
    public async Task SaveTranslationAsync_SerializesConcurrentCueUpdatesWithoutLoss()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var firstSavePaused = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstSave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSaveReachedWritePreparation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writePreparationCount = 0;
        Task? firstSave = null;
        Task? secondSave = null;

        async Task BeforeCheckpointWriteAsync()
        {
            if (Interlocked.Increment(ref writePreparationCount) == 1)
            {
                firstSavePaused.TrySetResult(true);
                await releaseFirstSave.Task;
                return;
            }

            secondSaveReachedWritePreparation.TrySetResult(true);
        }

        try
        {
            var service = new TranslationCheckpointService(
                NullLogger<TranslationCheckpointService>.Instance,
                root,
                BeforeCheckpointWriteAsync);

            firstSave = service.SaveTranslationAsync(42, "source-1", 1, "first translation", CancellationToken.None);
            await firstSavePaused.Task.WaitAsync(TimeSpan.FromSeconds(5));

            secondSave = service.SaveTranslationAsync(42, "source-1", 2, "second translation", CancellationToken.None);
            var secondSaveEnteredBeforeFirstReleased = secondSaveReachedWritePreparation.Task.IsCompleted;

            releaseFirstSave.TrySetResult(true);
            await Task.WhenAll(firstSave, secondSave);

            Assert.False(secondSaveEnteredBeforeFirstReleased);

            var checkpoint = await service.LoadAsync(42, "source-1", CancellationToken.None);

            Assert.NotNull(checkpoint);
            Assert.Equal("first translation", checkpoint.Translations[1]);
            Assert.Equal("second translation", checkpoint.Translations[2]);
        }
        finally
        {
            releaseFirstSave.TrySetResult(true);

            if (firstSave != null)
            {
                try
                {
                    await firstSave;
                }
                catch
                {
                }
            }

            if (secondSave != null)
            {
                try
                {
                    await secondSave;
                }
                catch
                {
                }
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveTranslationAsync_RejectsWriteWhenAttemptOwnerChangesDuringWritePreparation()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new LingarrDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var request = new TranslationRequest
        {
            Title = "Checkpoint ownership race",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.InProgress,
            IsActive = true,
            JobId = "old-attempt"
        };
        dbContext.TranslationRequests.Add(request);
        await dbContext.SaveChangesAsync();

        var writePreparationCount = 0;
        var staleWriteReachedPreparation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStaleWrite = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        async Task BeforeCheckpointWriteAsync()
        {
            if (Interlocked.Increment(ref writePreparationCount) == 2)
            {
                staleWriteReachedPreparation.TrySetResult(true);
                await releaseStaleWrite.Task;
            }
        }

        try
        {
            var service = new TranslationCheckpointService(
                NullLogger<TranslationCheckpointService>.Instance,
                root,
                BeforeCheckpointWriteAsync,
                dbContext);

            await service.SaveTranslationAsync(
                request.Id,
                "source-1",
                1,
                "valid translation",
                CancellationToken.None,
                "old-attempt");

            var staleSave = service.SaveTranslationAsync(
                request.Id,
                "source-1",
                2,
                "stale translation",
                CancellationToken.None,
                "old-attempt");

            await staleWriteReachedPreparation.Task.WaitAsync(TimeSpan.FromSeconds(5));
            request.JobId = "replacement-attempt";
            await dbContext.SaveChangesAsync();
            releaseStaleWrite.TrySetResult(true);

            await Assert.ThrowsAsync<OperationCanceledException>(() => staleSave);

            var checkpoint = await service.LoadAsync(request.Id, "source-1", CancellationToken.None);
            Assert.NotNull(checkpoint);
            Assert.Equal("valid translation", checkpoint!.Translations[1]);
            Assert.DoesNotContain(2, checkpoint.Translations.Keys);
        }
        finally
        {
            releaseStaleWrite.TrySetResult(true);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveCheckpointAsync_MergesConcurrentCueUpdatesWhilePreservingIntentionalRemovals()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new TranslationCheckpointService(
            NullLogger<TranslationCheckpointService>.Instance,
            root);

        try
        {
            await service.SaveCheckpointAsync(
                new TranslationCheckpoint
                {
                    TranslationRequestId = 42,
                    SourceFingerprint = "source-1",
                    Translations = new Dictionary<int, string>
                    {
                        [1] = "original line",
                        [2] = "remove this line"
                    }
                },
                CancellationToken.None);

            var staleCheckpoint = await service.LoadByRequestIdAsync(42, CancellationToken.None);
            Assert.NotNull(staleCheckpoint);

            staleCheckpoint!.Translations[1] = "edited line";
            staleCheckpoint.Translations.Remove(2);

            await service.SaveTranslationAsync(
                42,
                "source-1",
                3,
                "concurrent line",
                CancellationToken.None);

            await service.SaveCheckpointAsync(staleCheckpoint, CancellationToken.None);

            var checkpoint = await service.LoadAsync(42, "source-1", CancellationToken.None);

            Assert.NotNull(checkpoint);
            Assert.Equal("edited line", checkpoint.Translations[1]);
            Assert.DoesNotContain(2, checkpoint.Translations.Keys);
            Assert.Equal("concurrent line", checkpoint.Translations[3]);
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
    public async Task SaveCheckpointAsync_WhenLoadedCheckpointChangesSource_ClearsOldTranslationsAndSourcePreservedPositions()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new TranslationCheckpointService(
            NullLogger<TranslationCheckpointService>.Instance,
            root);

        try
        {
            await service.SaveCheckpointAsync(
                new TranslationCheckpoint
                {
                    TranslationRequestId = 44,
                    SourceFingerprint = "source-one",
                    Translations = new Dictionary<int, string>
                    {
                        [7] = "old source text"
                    },
                    SourcePreservedPositions = [7]
                },
                CancellationToken.None);

            var staleCheckpoint = await service.LoadByRequestIdAsync(44, CancellationToken.None);
            Assert.NotNull(staleCheckpoint);
            staleCheckpoint!.SourceFingerprint = "source-two";

            await service.SaveCheckpointAsync(staleCheckpoint, CancellationToken.None);

            var refreshedCheckpoint = await service.LoadAsync(44, "source-two", CancellationToken.None);

            Assert.NotNull(refreshedCheckpoint);
            Assert.Empty(refreshedCheckpoint!.Translations);
            Assert.Empty(refreshedCheckpoint.SourcePreservedPositions);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
