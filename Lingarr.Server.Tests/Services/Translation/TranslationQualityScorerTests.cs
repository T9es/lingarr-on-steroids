using System;
using System.IO;
using Lingarr.Server.Services.Translation;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class TranslationQualityScorerTests
{
    [Theory]
    [InlineData("en", "pl")]
    [InlineData("ja", "pl")]
    [InlineData("fr", "pl")]
    [InlineData("am", "he")]
    [InlineData("am", "so")]
    public void ScoreDirection_InitializesLanguageMappingsAndScoresSupportedPairs(
        string sourceLanguage,
        string targetLanguage)
    {
        var scorer = CreateScorerWithoutScoresFile();

        var score = scorer.ScoreDirection(sourceLanguage, targetLanguage);

        Assert.NotNull(score);
        Assert.InRange(score.Value, 0, 100);
    }

    [Fact]
    public void ScoreDirection_KeepsAmharicInTheSemiticFamily()
    {
        var scorer = CreateScorerWithoutScoresFile();

        var semiticScore = scorer.ScoreDirection("am", "he");
        var otherFamilyScore = scorer.ScoreDirection("am", "so");

        Assert.NotNull(semiticScore);
        Assert.NotNull(otherFamilyScore);
        Assert.True(semiticScore.Value > otherFamilyScore.Value);
    }

    [Theory]
    [InlineData("eng", "pol", "en", "pl")]
    [InlineData("jpn", "pl", "ja", "pl")]
    [InlineData("fra", "pl", "fr", "pl")]
    [InlineData(" EN-us ", "pl", "en", "pl")]
    public void ScoreDirection_NormalizesMediaLanguageAliases(
        string sourceLanguage,
        string targetLanguage,
        string canonicalSourceLanguage,
        string canonicalTargetLanguage)
    {
        var scorer = CreateScorerWithoutScoresFile();

        var canonicalScore = scorer.ScoreDirection(canonicalSourceLanguage, canonicalTargetLanguage);
        var aliasScore = scorer.ScoreDirection(sourceLanguage, targetLanguage);

        Assert.NotNull(aliasScore);
        Assert.Equal(canonicalScore, aliasScore);
    }

    [Theory]
    [InlineData("enx")]
    [InlineData("fiz")]
    [InlineData("tga")]
    public void ScoreDirection_DoesNotTruncateUnrecognizedLanguageCodes(string sourceLanguage)
    {
        var scorer = CreateScorerWithoutScoresFile();

        Assert.Null(scorer.ScoreDirection(sourceLanguage, "pl"));
    }

    [Theory]
    [InlineData("fil")]
    [InlineData("tgl")]
    public void ScoreDirection_DoesNotMisclassifyFilipinoOrTagalog(string sourceLanguage)
    {
        var scorer = CreateScorerWithoutScoresFile();

        var score = scorer.ScoreDirection(sourceLanguage, "pl");

        Assert.True(score == null || score == scorer.ScoreDirection("tl", "pl"));
    }

    private static TranslationQualityScorer CreateScorerWithoutScoresFile()
    {
        return new TranslationQualityScorer(Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"),
            "missing-scores.tsv"));
    }
}
