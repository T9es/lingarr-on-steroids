using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.Translation;
using Lingarr.Server.Services.Translation;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class TranslationPromptAugmenterTests
{
    [Fact]
    public async Task AugmentAsync_WhenOcrContextEnabled_AppendsConservativeOcrGuidance()
    {
        var settings = CreateSettings(enabled: true);
        var contextAccessor = new TranslationPromptContextAccessor
        {
            Current = new TranslationPromptContext
            {
                IsOcrDerivedSource = true,
                SeriesTitle = "The Legend of Korra",
                SeasonNumber = 1,
                EpisodeNumber = 1,
                EpisodeTitle = "Welcome to Republic City",
                SourceLanguage = "English",
                TargetLanguage = "Polish",
                SelectedStreamTitle = "English PGS",
                SourceSubtitleType = "full",
                SourceNote = "OCR from Blu-ray PGS subtitles"
            }
        };
        var augmenter = new TranslationPromptAugmenter(settings.Object, contextAccessor);

        var prompt = await augmenter.AugmentAsync("Translate from English to Polish.");

        Assert.Contains("Translate from English to Polish.", prompt);
        Assert.Contains("source subtitle text was produced by OCR", prompt);
        Assert.Contains("silently correct obvious OCR mistakes", prompt);
        Assert.Contains("Do not invent missing dialogue", prompt);
        Assert.Contains("The Legend of Korra", prompt);
        Assert.Contains("S01E01 - Welcome to Republic City", prompt);
        Assert.Contains("English PGS", prompt);
        Assert.Contains("OCR from Blu-ray PGS subtitles", prompt);
    }

    [Fact]
    public async Task AugmentAsync_WhenSettingDisabled_ReturnsOriginalPrompt()
    {
        var settings = CreateSettings(enabled: false);
        var contextAccessor = new TranslationPromptContextAccessor
        {
            Current = new TranslationPromptContext
            {
                IsOcrDerivedSource = true,
                MovieTitle = "American Pie"
            }
        };
        var augmenter = new TranslationPromptAugmenter(settings.Object, contextAccessor);

        var prompt = await augmenter.AugmentAsync("Translate this.");

        Assert.Equal("Translate this.", prompt);
    }

    [Fact]
    public async Task AugmentAsync_WhenSourceIsNotOcr_ReturnsOriginalPrompt()
    {
        var settings = CreateSettings(enabled: true);
        var contextAccessor = new TranslationPromptContextAccessor
        {
            Current = new TranslationPromptContext
            {
                IsOcrDerivedSource = false,
                MovieTitle = "American Pie"
            }
        };
        var augmenter = new TranslationPromptAugmenter(settings.Object, contextAccessor);

        var prompt = await augmenter.AugmentAsync("Translate this.");

        Assert.Equal("Translate this.", prompt);
    }

    private static Mock<ISettingService> CreateSettings(bool enabled)
    {
        var settings = new Mock<ISettingService>();
        settings
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrTranslationPromptEnabled))
            .ReturnsAsync(enabled ? "true" : "false");
        return settings;
    }
}
