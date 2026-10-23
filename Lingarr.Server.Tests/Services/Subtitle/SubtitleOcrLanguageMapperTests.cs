using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SubtitleOcrLanguageMapperTests
{
    [Theory]
    [InlineData("chi")]
    [InlineData("zh")]
    [InlineData("zho")]
    [InlineData("zh-CN")]
    [InlineData("ita")]
    [InlineData("ko")]
    [InlineData("kor")]
    [InlineData("und")]
    [InlineData("xxx")]
    [InlineData("")]
    public void TryMapToTesseractLanguage_WithUnsupportedLanguage_ReturnsFalse(string languageCode)
    {
        var mapped = SubtitleOcrLanguageMapper.TryMapToTesseractLanguage(
            languageCode,
            out var tesseractLanguage);

        Assert.False(mapped);
        Assert.Empty(tesseractLanguage);
    }

    [Fact]
    public void TryMapToTesseractLanguage_WithNullLanguage_ReturnsFalse()
    {
        var mapped = SubtitleOcrLanguageMapper.TryMapToTesseractLanguage(
            null,
            out var tesseractLanguage);

        Assert.False(mapped);
        Assert.Empty(tesseractLanguage);
    }

    [Theory]
    [InlineData("en", "eng")]
    [InlineData("eng", "eng")]
    [InlineData("en-US", "eng")]
    [InlineData("ja", "jpn")]
    [InlineData("jpn", "jpn")]
    [InlineData("jp", "jpn")]
    [InlineData("fr", "fra")]
    [InlineData("fre", "fra")]
    [InlineData("fra", "fra")]
    [InlineData("es", "spa")]
    [InlineData("spa", "spa")]
    [InlineData("de", "deu")]
    [InlineData("ger", "deu")]
    [InlineData("deu", "deu")]
    [InlineData("pl", "pol")]
    [InlineData("pol", "pol")]
    public void TryMapToTesseractLanguage_WithSupportedLanguage_ReturnsTesseractLanguage(
        string languageCode,
        string expected)
    {
        var mapped = SubtitleOcrLanguageMapper.TryMapToTesseractLanguage(
            languageCode,
            out var tesseractLanguage);

        Assert.True(mapped);
        Assert.Equal(expected, tesseractLanguage);
    }
}
