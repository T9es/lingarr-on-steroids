using System.Collections.Generic;
using Lingarr.Core.Entities;
using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SubtitleLanguageHelperTests
{
    [Fact]
    public void GetSupplementalOutputCaption_ShouldNotMarkForcedDialogueAsForced()
    {
        Assert.Null(SubtitleLanguageHelper.GetSupplementalOutputCaption(SubtitleLanguageHelper.TypeForcedDialogue));
        Assert.Equal(
            "forced",
            SubtitleLanguageHelper.GetSupplementalOutputCaption(SubtitleLanguageHelper.TypeForced));
        Assert.Equal(
            "signs",
            SubtitleLanguageHelper.GetSupplementalOutputCaption(SubtitleLanguageHelper.TypeSignsSongs));
    }

    [Fact]
    public void FindBestMatch_ShouldPreferGoodLowerPriority_OverGarbageHigherPriority()
    {
        // Arrange: English "Signs & Songs" vs Japanese "Full"
        var candidates = new List<EmbeddedSubtitle>
        {
            new()
            {
                Language = "eng",
                Title = "Signs & Songs",
                StreamIndex = 0,
                IsTextBased = true,
                IsForced = false,
                IsDefault = false,
                CodecName = "subrip"
            },
            new()
            {
                Language = "jpn",
                Title = "Full",
                StreamIndex = 1,
                IsTextBased = true,
                IsForced = false,
                IsDefault = false,
                CodecName = "subrip"
            }
        };

        var configuredLanguages = new List<string> { "en", "ja" };

        // Act
        var result = SubtitleLanguageHelper.FindBestMatch(candidates, configuredLanguages);

        // Assert
        // Japanese "Full" should win over English "Signs & Songs" because:
        // - English score: 50 (match) - 40 (signs) + 5 (not forced) = 15 (below QualityThreshold of 40, no priority bonus)
        // - Japanese score: 50 (match) + 25 (full) + 5 (not forced) = 80 + 80 (priority bonus for 2nd lang) = 160
        Assert.Equal("ja", result.MatchedLanguage);
        Assert.Equal(1, result.Subtitle?.StreamIndex);
    }

    [Fact]
    public void FindBestMatch_ShouldPreferHigherPriority_WhenBothAreGood()
    {
        // Arrange: Both English and Japanese are full dialogue tracks
        var candidates = new List<EmbeddedSubtitle>
        {
            new()
            {
                Language = "eng",
                Title = "English",
                StreamIndex = 0,
                IsTextBased = true,
                IsForced = false,
                IsDefault = true,
                CodecName = "subrip"
            },
            new()
            {
                Language = "jpn",
                Title = "Japanese",
                StreamIndex = 1,
                IsTextBased = true,
                IsForced = false,
                IsDefault = false,
                CodecName = "subrip"
            }
        };

        var configuredLanguages = new List<string> { "en", "ja" };

        // Act
        var result = SubtitleLanguageHelper.FindBestMatch(candidates, configuredLanguages);

        // Assert
        // English should win because both are good quality and English has higher priority
        // - English score: 50 (match) + 5 (not forced) + 5 (default) = 60 + 160 (priority bonus for 1st lang) = 220
        // - Japanese score: 50 (match) + 5 (not forced) = 55 + 80 (priority bonus for 2nd lang) = 135
        Assert.Equal("en", result.MatchedLanguage);
        Assert.Equal(0, result.Subtitle?.StreamIndex);
    }

    [Fact]
    public void FindBestMatch_ShouldReturnNull_WhenNoMatchingLanguage()
    {
        // Arrange: Only German subtitle, but looking for English/Japanese
        var candidates = new List<EmbeddedSubtitle>
        {
            new()
            {
                Language = "ger",
                Title = "German",
                StreamIndex = 0,
                IsTextBased = true,
                IsForced = false,
                IsDefault = false,
                CodecName = "subrip"
            }
        };

        var configuredLanguages = new List<string> { "en", "ja" };

        // Act
        var result = SubtitleLanguageHelper.FindBestMatch(candidates, configuredLanguages);

        // Assert
        Assert.Null(result.Subtitle);
        Assert.Equal(string.Empty, result.MatchedLanguage);
    }

    [Fact]
    public void FindBestMatch_ShouldHandleForcedTracks()
    {
        // Arrange: Forced English vs Normal Japanese
        var candidates = new List<EmbeddedSubtitle>
        {
            new()
            {
                Language = "eng",
                Title = "English Forced",
                StreamIndex = 0,
                IsTextBased = true,
                IsForced = true, // Forced tracks get penalized
                IsDefault = false,
                CodecName = "subrip"
            },
            new()
            {
                Language = "jpn",
                Title = "Japanese Full",
                StreamIndex = 1,
                IsTextBased = true,
                IsForced = false,
                IsDefault = false,
                CodecName = "subrip"
            }
        };

        var configuredLanguages = new List<string> { "en", "ja" };

        // Act
        var result = SubtitleLanguageHelper.FindBestMatch(candidates, configuredLanguages);

        // Assert
        // Japanese should win because:
        // - English forced score: 50 (match) - 50 (forced penalty) = 0 (below QualityThreshold of 30, no priority bonus)
        // - Japanese score: 50 (match) + 5 (not forced) = 55 + 80 (priority bonus) = 135
        Assert.Equal("ja", result.MatchedLanguage);
        Assert.Equal(1, result.Subtitle?.StreamIndex);
    }
    
    [Fact]
    public void FindBestMatch_ShouldPreferBetterQuality_WithinSameLanguage()
    {
        // Arrange: Two English tracks - full vs signs
        var candidates = new List<EmbeddedSubtitle>
        {
            new()
            {
                Language = "eng",
                Title = "Signs & Songs",
                StreamIndex = 0,
                IsTextBased = true,
                IsForced = true,
                IsDefault = false,
                CodecName = "subrip"
            },
            new()
            {
                Language = "eng",
                Title = "Full Subtitles",
                StreamIndex = 1,
                IsTextBased = true,
                IsForced = false,
                IsDefault = true,
                CodecName = "subrip"
            }
        };

        var configuredLanguages = new List<string> { "en" };

        // Act
        var result = SubtitleLanguageHelper.FindBestMatch(candidates, configuredLanguages);

        // Assert
        // Full subtitles should win over Signs & Songs
        Assert.Equal("en", result.MatchedLanguage);
        Assert.Equal(1, result.Subtitle?.StreamIndex);
    }

    [Fact]
    public void ScoreSubtitleCandidate_ShouldPenalizeSDH()
    {
        var subtitle = new EmbeddedSubtitle { Language = "eng", Title = "English SDH", IsForced = false };
        // Base 50 (match) + 5 (not forced) + 5 (default? no) - 10 (SDH) = 45
        var score = SubtitleLanguageHelper.ScoreSubtitleCandidate(subtitle, "en");
        
        // Compare with Clean: 50 + 5 = 55. SDH should be lower.
        Assert.Equal(45, score);
    }

    [Fact]
    public void FindBestMatch_ShouldPreferClean_OverSDH()
    {
        // Arrange: "English SDH" vs "English Clean"
        var candidates = new List<EmbeddedSubtitle>
        {
            new() { Language = "eng", Title = "English SDH", StreamIndex = 0 },
            new() { Language = "eng", Title = "English", StreamIndex = 1 }
        };

        var result = SubtitleLanguageHelper.FindBestMatch(candidates, new List<string> { "en" });
        
        // Clean track should win because SDH is penalized (-10)
        Assert.Equal("en", result.MatchedLanguage);
        Assert.Equal(1, result.Subtitle?.StreamIndex);
    }

    [Fact]
    public void FindBestMatch_ShouldStillSelectSDH_IfLanguagePriorityRequiresIt()
    {
        // Arrange: English SDH (Forced & SDH) vs Japanese Clean
        // English is P1, Japanese P2.
        // With new -50 forced penalty:
        // English score: 50 (match) - 10 (SDH) - 50 (Forced) = -10 (below QualityThreshold, no priority bonus)
        // Japanese score: 50 (match) + 5 (clean) = 55 + 80 (priority bonus) = 135.
        // Japanese should win because English forced score is below threshold.
        
        var candidates = new List<EmbeddedSubtitle>
        {
            new() { Language = "eng", Title = "English SDH", IsForced = true, StreamIndex = 0 },
            new() { Language = "ja", Title = "Japanese", IsForced = false, StreamIndex = 1 }
        };

        var result = SubtitleLanguageHelper.FindBestMatch(candidates, new List<string> { "en", "ja" });
        
        Assert.Equal("ja", result.MatchedLanguage);
        Assert.Equal(1, result.Subtitle?.StreamIndex);
    }

    [Theory]
    [InlineData("Movie.en.srt", "en")]
    [InlineData("Movie.eng.ass", "en")]
    [InlineData("Show.S01E01.[pl].vtt", "pl")]
    [InlineData("Episode_ja.ssa", "ja")]
    [InlineData("Film.pt-BR.srt", "pt")]
    public void DetectLanguageFromFileName_ShouldRecognizeSupportedPatterns(string fileName, string expectedLanguage)
    {
        var detectedLanguage = SubtitleLanguageHelper.DetectLanguageFromFileName(fileName);

        Assert.Equal(expectedLanguage, detectedLanguage);
    }

    [Theory]
    [InlineData("queen.srt")]
    [InlineData("plane.ass")]
    [InlineData("Movie.ass.srt")]
    [InlineData("Movie.sub.srt")]
    [InlineData("railgun-e01-60s.embedded.ass")]
    public void DetectLanguageFromFileName_ShouldAvoidFalsePositives(string fileName)
    {
        var detectedLanguage = SubtitleLanguageHelper.DetectLanguageFromFileName(fileName);

        Assert.Null(detectedLanguage);
    }

    [Fact]
    public void DetectLanguageFromFileName_ShouldRespectConfiguredLanguages()
    {
        var detectedLanguage = SubtitleLanguageHelper.DetectLanguageFromFileName(
            "Movie.de.srt",
            ["en", "pl"]);

        Assert.Null(detectedLanguage);
    }

    [Theory]
    [InlineData("New Amsterdam (2018) - S05E06 - Give Me a Sign [WEBDL-1080p][EAC3 5.pl.-ai-sztuczna-inteligencja-.srt")]
    [InlineData("The Walking Dead (2010) - S09E03 - Warning Signs [WEBDL-1080p][EAC3 5.pl.-ai-sztuczna-inteligencja-.srt")]
    [InlineData("Toradora! (2008) - S01E03 - 003 - Your Song [Bluray-1080p][FLAC 2.pl.-ai-sztuczna-inteligencja-.srt")]
    [InlineData("Pokemon (1997) - S20E31 - 1253 - Song Within the Mist [WEBDL-1080p][AAC 2.pl.-ai-sztuczna-inteligencja-.ass")]
    public void DetermineSubtitleTypeFromFilename_ShouldNotTreatMediaTitleKeywordsAsSupplemental(string fileName)
    {
        var subtitleType = SubtitleLanguageHelper.DetermineSubtitleTypeFromFilename(fileName);

        Assert.Equal(SubtitleLanguageHelper.TypeFull, subtitleType);
    }

    [Theory]
    [InlineData("Movie.pl.forced.-ai-sztuczna-inteligencja-.srt", SubtitleLanguageHelper.TypeForced)]
    [InlineData("Movie.en.signs.srt", SubtitleLanguageHelper.TypeSignsSongs)]
    [InlineData("Movie.en.signs-and-songs.srt", SubtitleLanguageHelper.TypeSignsSongs)]
    [InlineData("Movie.jpn.karaoke.ass", SubtitleLanguageHelper.TypeSignsSongs)]
    [InlineData("Movie.en.sdh.srt", SubtitleLanguageHelper.TypeSdh)]
    [InlineData("Movie.en.cc.srt", SubtitleLanguageHelper.TypeClosedCaptions)]
    [InlineData("Movie.en.commentary.srt", SubtitleLanguageHelper.TypeCommentary)]
    public void DetermineSubtitleTypeFromFilename_ShouldUseSubtitleSuffixAfterLanguageToken(
        string fileName,
        string expectedType)
    {
        var subtitleType = SubtitleLanguageHelper.DetermineSubtitleTypeFromFilename(fileName);

        Assert.Equal(expectedType, subtitleType);
    }
}
