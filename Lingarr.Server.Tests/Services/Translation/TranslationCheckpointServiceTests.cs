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
}
