using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Server.Services.Translation;
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
}
