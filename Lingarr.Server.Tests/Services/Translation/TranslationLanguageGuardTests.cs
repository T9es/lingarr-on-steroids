using System.Collections.Generic;
using System.Linq;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Translation;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class TranslationLanguageGuardTests
{
    // Exact provider translation captured from a live Gemini batch run for The Batman
    // (movie 3, request 3031459, position 989, ro -> pl). It is a valid Polish translation
    // that uses typographic quotes, which the guard must not treat as mojibake.
    private const string BatmanProviderTranslation =
        "Tylko ja tu znam różnicę\nmiędzy „el” a „la”?";

    // Provider variant for Yes Man (2008) (movie 556, request 3031552, position 476, es -> pl)
    // using typographic quotes around the quoted nicknames.
    private const string YesManQuoteProviderTranslation =
        "Połączyłem „brata” z „Mario”.\nBratario.";

    // UTF-8 Polish text decoded as Latin-1: every Å/Ã/Ä lead is followed by the Latin-1
    // rendering of a continuation byte. This is real mojibake and must still be rejected.
    private const string Latin1MojibakePolish =
        "ZaÅ¼Ã³Å‚Ä‡ gÄ™Å›lÄ… jaÅºÅ„";

    // UTF-8 typographic quotes decoded as Latin-1: each 3-byte sequence leaves two C1
    // control characters (0x80-0x9F), which are unambiguous mis-decoding evidence.
    private const string ControlCharacterMojibake =
        "â\u0080\u009ETo jest testâ\u0080\u009D";

    // U+FFFD replacement characters are what a lossy decode leaves behind.
    private const string ReplacementCharacterMojibake =
        "Tekst \uFFFD\uFFFD\uFFFD błąd";

    // Exact target strings from SubtitleIntegrityServiceAssVerificationTests,
    // ValidateIntegrityAsync_WhenTargetContainsMojibake_ReturnsFalse. They are Polish UTF-8
    // text decoded as Windows-1250/Latin-2, so UTF-8 lead bytes render as Ä and Ĺ and the
    // continuation bytes render as Ľ, ‡, ™, ş and ›.
    private static readonly string[] Latin2MojibakeTargetLines =
    [
        "Shizuri bez wahania porzuci gr\u00C4\u2122",
        "\u0139\u013Deby tylko nie by\u00C4\u2021 ci\u00C4\u2122\u0139\u013Darem",
        "Daje z siebie wszystko, by utrzyma\u00C4\u2021 \u0139\u013Dycie",
        "ale ja te\u0139\u013D mog\u00C4\u2122 co\u0139\u203A zrobi\u00C4\u2021",
        "Sied\u0139\u015F cicho, \u0139\u013Deby\u0139\u203Amy mogli ci\u00C4\u2122 porwa\u00C4\u2021"
    ];

    [Fact]
    public void AnalyzeBatch_WhenValidPolishTranslationUsesTypographicQuotes_DoesNotFlagLanguageMismatch()
    {
        var analysis = Analyze(
            "Cred că doar eu știu diferența dintre \"el\" și \"la\"?",
            BatmanProviderTranslation,
            targetLanguage: "pl");

        Assert.Equal(1, analysis.ComparableCount);
        Assert.Empty(analysis.MismatchedPositions);
        Assert.False(analysis.IsMostlyMismatched);
    }

    [Fact]
    public void AnalyzeBatch_WhenValidPolishTranslationUsesStraightQuotes_DoesNotFlagLanguageMismatch()
    {
        var analysis = Analyze(
            "Cred că doar eu știu diferența dintre \"el\" și \"la\"?",
            "Tylko ja tu znam różnicę\nmiędzy \"el\" a \"la\"?",
            targetLanguage: "pl");

        Assert.Equal(1, analysis.ComparableCount);
        Assert.Empty(analysis.MismatchedPositions);
    }

    [Fact]
    public void AnalyzeBatch_WhenQuotedNicknamesUseTypographicQuotes_DoesNotFlagLanguageMismatch()
    {
        var analysis = Analyze(
            "Uní \"hermano\" con \"Mario\".\nHermario.",
            YesManQuoteProviderTranslation,
            targetLanguage: "pl");

        Assert.Equal(1, analysis.ComparableCount);
        Assert.Empty(analysis.MismatchedPositions);
    }

    [Fact]
    public void AnalyzeBatch_WhenClusterOfValidTranslationsUsesTypographicQuotes_DoesNotFlagMismatch()
    {
        var sourceItems = Enumerable.Range(901, 8)
            .Select(position => new BatchSubtitleItem
            {
                Position = position,
                Line = $"Source line {position}"
            })
            .ToList();
        var translations = sourceItems.ToDictionary(
            item => item.Position,
            item => (string)$"Powiedział „{item.Line}” i wyszedł.");

        var analysis = TranslationLanguageGuard.AnalyzeBatch(sourceItems, translations, "pl");

        Assert.Equal(8, analysis.ComparableCount);
        Assert.Empty(analysis.MismatchedPositions);
        Assert.False(analysis.IsMostlyMismatched);
    }

    [Fact]
    public void AnalyzeBatch_WhenPolishTextHasAccentsEllipsisAndDashes_DoesNotFlagLanguageMismatch()
    {
        var analysis = Analyze(
            "Cóż, cóż... to nie ma sensu – naprawdę.",
            "Cóż, cóż... to nie ma sensu – naprawdę.",
            targetLanguage: "pl");

        Assert.Empty(analysis.MismatchedPositions);
    }

    [Fact]
    public void AnalyzeBatch_WhenGermanTextHasUmlauts_DoesNotFlagLanguageMismatch()
    {
        var analysis = Analyze(
            "Können Sie das bitte wiederholen?",
            "Schöne Grüße aus München, können Sie das wiederholen?",
            targetLanguage: "de");

        Assert.Empty(analysis.MismatchedPositions);
    }

    [Fact]
    public void AnalyzeBatch_WhenNordicTextUsesRingAccent_DoesNotFlagLanguageMismatch()
    {
        var analysis = Analyze(
            "How are you today?",
            "Går det bra? Åke är här idag.",
            targetLanguage: "sv");

        Assert.Empty(analysis.MismatchedPositions);
    }

    [Fact]
    public void AnalyzeBatch_WhenProviderTextIsLatin1Mojibake_StillFlagsPosition()
    {
        var analysis = Analyze(
            "Tylko ja tu znam różnicę między \"el\" a \"la\"?",
            Latin1MojibakePolish,
            targetLanguage: "pl");

        Assert.Contains(1, analysis.MismatchedPositions);
    }

    [Fact]
    public void AnalyzeBatch_WhenProviderTextContainsC1ControlMojibake_StillFlagsPosition()
    {
        var analysis = Analyze(
            "This is only a test.",
            ControlCharacterMojibake,
            targetLanguage: "pl");

        Assert.Contains(1, analysis.MismatchedPositions);
    }

    [Fact]
    public void AnalyzeBatch_WhenProviderTextContainsReplacementCharacters_StillFlagsPosition()
    {
        var analysis = Analyze(
            "This is only a test.",
            ReplacementCharacterMojibake,
            targetLanguage: "pl");

        Assert.Contains(1, analysis.MismatchedPositions);
    }

    [Fact]
    public void AnalyzeSubtitles_WhenTargetIsPolishUtf8DecodedAsLatin2_FlagsCluster()
    {
        var source = new List<SubtitleItem>
        {
            Item(1, "Shizuri will give up a game she loves without question"),
            Item(2, "just so she will not be a burden to me"),
            Item(3, "And she is doing her best to maintain our lifestyle"),
            Item(4, "but there are things I can do for Shizuri too"),
            Item(5, "Stay quiet so we can kidnap you")
        };
        var target = Latin2MojibakeTargetLines
            .Select((line, index) => Item(index + 1, line))
            .ToList();

        var analysis = TranslationLanguageGuard.AnalyzeSubtitles(source, target, "pl");

        Assert.True(analysis.IsMostlyMismatched);
        Assert.Contains(2, analysis.MismatchedPositions);
        Assert.Contains(5, analysis.MismatchedPositions);
    }

    [Fact]
    public void AnalyzeBatch_WhenEastAsianTargetReturnsLatinText_StillFlagsPosition()
    {
        var analysis = Analyze(
            "This is only a test.",
            "This is still English text.",
            targetLanguage: "zh");

        Assert.Contains(1, analysis.MismatchedPositions);
    }

    [Fact]
    public void AnalyzeBatch_WhenEastAsianTargetReturnsChineseText_DoesNotFlagPosition()
    {
        var analysis = Analyze(
            "This is only a test.",
            "这只是一个测试。",
            targetLanguage: "zh");

        Assert.Empty(analysis.MismatchedPositions);
    }

    private static SubtitleItem Item(int position, string line)
    {
        return new SubtitleItem
        {
            Position = position,
            Lines = [line],
            PlaintextLines = [line]
        };
    }

    private static TranslationLanguageAnalysis Analyze(
        string sourceText,
        string translatedText,
        string targetLanguage)
    {
        var sourceItems = new List<BatchSubtitleItem>
        {
            new()
            {
                Position = 1,
                Line = sourceText
            }
        };
        var translatedByPosition = new Dictionary<int, string>
        {
            [1] = translatedText
        };

        return TranslationLanguageGuard.AnalyzeBatch(sourceItems, translatedByPosition, targetLanguage);
    }
}
