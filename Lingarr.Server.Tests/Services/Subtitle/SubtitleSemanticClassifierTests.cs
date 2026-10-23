using System.Collections.Generic;
using System.Linq;
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

    private static readonly string[] ChineseDialogueSamples =
    [
        "技能是上古之果在远古时代赐予人类的特殊能力",
        "双挥或者迅捷移动感觉很酷",
        "完美命中也不错",
        "总之我肯定想要战斗型技能",
        "毕竟我一直想成为冒险者",
        "这是你小时候的梦想吧？",
        "我希望能得到一个可以和你从事同样职业的技能",
        "那我们就一起成为冒险者吧！",
        "我们会成为最强的冒险者",
        "我的辉煌冒险人生就此开始！",
        "然后拿到那个大赞助商和直升机",
        "公元2008年7月人类面临灭绝的危机。"
    ];

    [Theory]
    [InlineData("技能是上古之果在远古时代赐予人类的特殊能力")]
    [InlineData("双挥或者迅捷移动感觉很酷")]
    [InlineData("赶紧把路修好然后离开这里")]
    [InlineData("公元2008年7月人类面临灭绝的危机。")]
    public void IsLikelyCorruptText_WhenChineseDialogueHasNoLatinLetters_ReturnsFalse(string cue)
    {
        Assert.False(SubtitleSemanticClassifier.IsLikelyCorruptText(cue));
    }

    [Fact]
    public void IsLikelyCorruptText_ShortChineseTrack_HasNoCorruptEntries()
    {
        var corruptEntries = ChineseDialogueSamples.Count(SubtitleSemanticClassifier.IsLikelyCorruptText);

        Assert.Equal(0, corruptEntries);
    }

    [Theory]
    [InlineData("AAAAAAAAAAAA BBBBBBBBBBBB")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("bcdfghjklmnpqrstw")]
    [InlineData("bcdfghjklmnpqrstw bcdfghjklmnpqrstw")]
    [InlineData("好好好好好好好好 好好好好好好好好")]
    [InlineData("Hello 你好 こんにちは Привет")]
    public void IsLikelyCorruptText_WhenTextIsGenuinelyCorrupt_ReturnsTrue(string text)
    {
        Assert.True(SubtitleSemanticClassifier.IsLikelyCorruptText(text));
    }

    [Fact]
    public void Analyze_WhenTrackIsChineseDialogue_ReturnsHealthyWithoutCorruptEntries()
    {
        var subtitles = Enumerable.Range(0, 60)
            .Select(index =>
            {
                var line = ChineseDialogueSamples[index % ChineseDialogueSamples.Length];
                return new SubtitleItem
                {
                    Lines = [line],
                    PlaintextLines = [line]
                };
            })
            .ToList();

        var analysis = SubtitleSourceHealthAnalyzer.Analyze(subtitles);

        Assert.Equal(SubtitleSourceHealthStatus.Healthy, analysis.Status);
        Assert.Equal(0, analysis.CorruptEntries);
    }
}
