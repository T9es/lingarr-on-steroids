using System.Collections.Generic;
using System.Linq;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Services.Translation;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class TranslationEchoGuardTests
{
    [Fact]
    public void AnalyzeBatch_WhenUnchangedProperNameOnlyCues_ReturnsNoEchoes()
    {
        var sourceItems = new List<BatchSubtitleItem>
        {
            new() { Position = 1, Line = "Michael! Michael! Michael!" },
            new() { Position = 2, Line = "Mae, Mae, Mae, Mae." },
            new() { Position = 3, Line = "- Bolin: ASAMI! KORRA!" },
            new() { Position = 4, Line = "DR. CULLEN: Jasper?" },
            new() { Position = 5, Line = "Karl Allen Gibbs." }
        };
        var translatedByPosition = sourceItems.ToDictionary(item => item.Position, item => item.Line);

        var analysis = TranslationEchoGuard.AnalyzeBatch(
            sourceItems,
            translatedByPosition,
            sourceLanguage: "en",
            targetLanguage: "pl");

        Assert.Equal(0, analysis.EchoedCount);
        Assert.Empty(analysis.EchoedPositions);
    }

    [Fact]
    public void AnalyzeBatch_WhenNormalDialogueEchoesSource_StillFlagsEchoes()
    {
        var sourceItems = new List<BatchSubtitleItem>
        {
            new() { Position = 1, Line = "Hello, my friend" },
            new() { Position = 2, Line = "We need to go home" },
            new() { Position = 3, Line = "This is very important" }
        };
        var translatedByPosition = sourceItems.ToDictionary(item => item.Position, item => item.Line);

        var analysis = TranslationEchoGuard.AnalyzeBatch(
            sourceItems,
            translatedByPosition,
            sourceLanguage: "en",
            targetLanguage: "pl");

        Assert.Equal([1, 2, 3], analysis.EchoedPositions);
        Assert.True(analysis.IsMostlyEchoed);
    }
}
