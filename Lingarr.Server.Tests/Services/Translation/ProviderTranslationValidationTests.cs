using System.Collections.Generic;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Services.Translation;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class ProviderTranslationValidationTests
{
    [Fact]
    public void Analyze_WhenProviderUsesTypographicQuotesInPolishTranslation_ReturnsNoInvalidPositions()
    {
        var sourceItems = new List<BatchSubtitleItem>
        {
            new()
            {
                Position = 989,
                Line = "Cred că doar eu știu diferența dintre \"el\" și \"la\"?"
            }
        };
        var translatedByPosition = new Dictionary<int, string>
        {
            [989] = "Tylko ja tu znam różnicę\nmiędzy „el” a „la”?"
        };

        var result = ProviderTranslationValidation.Analyze(
            sourceItems,
            translatedByPosition,
            sourceLanguage: "ro",
            targetLanguage: "pl");

        Assert.Empty(result.InvalidPositions);
        Assert.Empty(result.EchoedPositions);
        Assert.Empty(result.MismatchedPositions);
    }

    [Fact]
    public void Analyze_WhenProviderReturnsRealMojibake_StillRejectsPosition()
    {
        var sourceItems = new List<BatchSubtitleItem>
        {
            new()
            {
                Position = 476,
                Line = "Uní \"hermano\" con \"Mario\".\nHermario."
            }
        };
        var translatedByPosition = new Dictionary<int, string>
        {
            [476] = "ZaÅ¼Ã³Å‚Ä‡ gÄ™Å›lÄ… jaÅºÅ„"
        };

        var result = ProviderTranslationValidation.Analyze(
            sourceItems,
            translatedByPosition,
            sourceLanguage: "es",
            targetLanguage: "pl");

        Assert.Contains(476, result.InvalidPositions);
        Assert.Contains(476, result.MismatchedPositions);
        Assert.DoesNotContain(476, result.EchoedPositions);
    }

    [Fact]
    public void Analyze_WhenOneOfTwoProviderCuesIsMojibake_RejectsOnlyThatPosition()
    {
        var sourceItems = new List<BatchSubtitleItem>
        {
            new()
            {
                Position = 988,
                Line = "Where does he come from?"
            },
            new()
            {
                Position = 989,
                Line = "Cred că doar eu știu diferența dintre \"el\" și \"la\"?"
            }
        };
        var translatedByPosition = new Dictionary<int, string>
        {
            [988] = "Skąd on pochodzi?",
            [989] = "ZaÅ¼Ã³Å‚Ä‡ gÄ™Å›lÄ… jaÅºÅ„"
        };

        var result = ProviderTranslationValidation.Analyze(
            sourceItems,
            translatedByPosition,
            sourceLanguage: "en",
            targetLanguage: "pl");

        Assert.Contains(989, result.InvalidPositions);
        Assert.DoesNotContain(988, result.InvalidPositions);
    }
}
