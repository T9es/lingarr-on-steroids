using System;
using System.Collections.Generic;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class SubtitleOcrQualityAnalyzerTests
{
    [Fact]
    public void Analyze_WithHealthyDialogue_AcceptsOutput()
    {
        var subtitles = BuildSubtitles(80, index => $"This is readable dialogue line {index}.");

        var result = SubtitleOcrQualityAnalyzer.Analyze(subtitles, minQualityScore: 80, allowSparse: false);

        Assert.True(result.Accepted);
        Assert.Equal(80, result.CueCount);
        Assert.True(result.QualityScore >= 80);
    }

    [Fact]
    public void Analyze_WithSparseDialogue_BlocksOutput()
    {
        var subtitles = BuildSubtitles(10, index => $"Short sign {index}");

        var result = SubtitleOcrQualityAnalyzer.Analyze(subtitles, minQualityScore: 80, allowSparse: false);

        Assert.False(result.Accepted);
        Assert.Contains("Only 10 cues", result.IssueSummary);
    }

    [Fact]
    public void TryMapToTesseractLanguage_MapsCommonCodes()
    {
        Assert.True(SubtitleOcrLanguageMapper.TryMapToTesseractLanguage("en", out var english));
        Assert.Equal("eng", english);
        Assert.True(SubtitleOcrLanguageMapper.TryMapToTesseractLanguage("jpn", out var japanese));
        Assert.Equal("jpn", japanese);
        Assert.True(SubtitleOcrLanguageMapper.TryMapToTesseractLanguage("fre", out var french));
        Assert.Equal("fra", french);
        Assert.True(SubtitleOcrLanguageMapper.TryMapToTesseractLanguage("es", out var spanish));
        Assert.Equal("spa", spanish);
        Assert.True(SubtitleOcrLanguageMapper.TryMapToTesseractLanguage("ger", out var german));
        Assert.Equal("deu", german);
        Assert.True(SubtitleOcrLanguageMapper.TryMapToTesseractLanguage("pl", out var polish));
        Assert.Equal("pol", polish);
    }

    private static List<SubtitleItem> BuildSubtitles(int count, Func<int, string> textFactory)
    {
        var result = new List<SubtitleItem>();
        for (var index = 0; index < count; index++)
        {
            result.Add(new SubtitleItem
            {
                Position = index + 1,
                StartTime = index * 1000,
                EndTime = index * 1000 + 800,
                Lines = [textFactory(index)],
                PlaintextLines = [textFactory(index)]
            });
        }

        return result;
    }
}
