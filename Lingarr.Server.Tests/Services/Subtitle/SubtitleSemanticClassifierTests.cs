using System.Collections.Generic;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SubtitleSemanticClassifierTests
{
    [Fact]
    public void IsSafeSourceEcho_WhenAssStyleMarksSignOrKaraoke_ReturnsTrue()
    {
        var sign = new SubtitleItem
        {
            Lines = ["{\\an8}SHOW TITLE"],
            PlaintextLines = ["SHOW TITLE"],
            SsaDialogue = new SsaDialogue { Style = "Signs" }
        };
        var karaoke = new SubtitleItem
        {
            Lines = ["{\\k20}La la"],
            PlaintextLines = ["La la"],
            SsaDialogue = new SsaDialogue { Style = "Karaoke" }
        };

        Assert.True(SubtitleSemanticClassifier.IsSafeSourceEcho(sign, "SHOW TITLE", "SHOW TITLE"));
        Assert.True(SubtitleSemanticClassifier.IsSafeSourceEcho(karaoke, "La la", "La la"));
    }

    [Fact]
    public void IsSafeSourceEcho_WhenCueIsOrdinaryDialogue_ReturnsFalse()
    {
        var dialogue = new SubtitleItem
        {
            Lines = ["This is dialogue"],
            PlaintextLines = ["This is dialogue"],
            SsaDialogue = new SsaDialogue { Style = "Default" }
        };

        Assert.False(SubtitleSemanticClassifier.IsSafeSourceEcho(
            dialogue,
            "This is dialogue",
            "This is dialogue"));
    }

    [Theory]
    [InlineData("[laughter]")]
    [InlineData("(LAUGHS)")]
    [InlineData("[door]")]
    [InlineData("(DOORS)")]
    [InlineData("[creaks]")]
    [InlineData("(footsteps)")]
    [InlineData("[door creaks]")]
    public void IsSafeSourceEcho_WhenCommonStandaloneSoundEffectIsEchoed_ReturnsTrue(string cue)
    {
        Assert.True(SubtitleSemanticClassifier.IsSafeSourceEcho(null, cue, cue));
    }

    [Theory]
    [InlineData("I heard footsteps behind the door.")]
    [InlineData("Please close the door.")]
    [InlineData("(I heard footsteps)")]
    [InlineData("[The door is open]")]
    public void Classify_WhenSoundEffectWordAppearsInOrdinaryDialogue_ReturnsDialogue(string text)
    {
        var classification = SubtitleSemanticClassifier.Classify(null, text);

        Assert.Equal(SubtitleSemanticKind.Dialogue, classification.Kind);
        Assert.False(SubtitleSemanticClassifier.IsSafeSourceEcho(null, text, text));
    }

    [Fact]
    public void Classify_PreservesSignTitleAndLyricSemantics()
    {
        var sign = new SubtitleItem
        {
            SsaDialogue = new SsaDialogue { Style = "Signs" }
        };
        var lyrics = new SubtitleItem
        {
            SsaDialogue = new SsaDialogue { Style = "Karaoke" }
        };

        Assert.Equal(SubtitleSemanticKind.SignOrTitle, SubtitleSemanticClassifier.Classify(null, "SHOW-TITLE").Kind);
        Assert.Equal(SubtitleSemanticKind.SignOrTitle, SubtitleSemanticClassifier.Classify(sign, "ON SCREEN").Kind);
        Assert.Equal(SubtitleSemanticKind.LyricOrChant, SubtitleSemanticClassifier.Classify(lyrics, "La la").Kind);
    }

    [Theory]
    [InlineData("NO MALIHINI OHANA")]
    [InlineData("TOOKIE BAH WABA!")]
    [InlineData("ALOHA, E KOMO MAI")]
    [InlineData("I LAILA 'O KAUA'I LA")]
    public void Classify_HawaiianChantLines_AreLyricOrChant(string cue)
    {
        var classification = SubtitleSemanticClassifier.Classify(null, cue);

        Assert.Equal(SubtitleSemanticKind.LyricOrChant, classification.Kind);
        Assert.True(classification.ShouldRequestProvider);
        Assert.True(classification.CanPreserveSourceWhenProviderMissing);
    }

    [Fact]
    public void Classify_WhenTextIsRepeatedAcrossFile_IsPreservableLyricOrChant()
    {
        var repeatedProviderTexts = new HashSet<string> { "We did it again!" };

        var classification = SubtitleSemanticClassifier.Classify(
            null,
            "We did it again!",
            repeatedProviderTexts: repeatedProviderTexts);

        Assert.Equal(SubtitleSemanticKind.LyricOrChant, classification.Kind);
        Assert.True(classification.ShouldRequestProvider);
        Assert.True(classification.CanPreserveSourceWhenProviderMissing);
    }

    [Fact]
    public void Classify_WhenTextIsNotRepeatedAcrossFile_RemainsOrdinaryDialogue()
    {
        var classification = SubtitleSemanticClassifier.Classify(null, "We did it again!");

        Assert.Equal(SubtitleSemanticKind.Dialogue, classification.Kind);
        Assert.True(classification.ShouldRequestProvider);
        Assert.False(classification.CanPreserveSourceWhenProviderMissing);
    }
}
