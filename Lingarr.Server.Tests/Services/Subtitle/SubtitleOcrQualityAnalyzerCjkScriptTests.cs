using System;
using System.Collections.Generic;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SubtitleOcrQualityAnalyzerCjkScriptTests
{
    private const string ScriptMismatchIssue =
        "OCR output script does not match expected CJK source language.";

    [Fact]
    public void Analyze_WithLatinDialogueForExpectedChinese_RejectsScriptMismatch()
    {
        var subtitles = BuildSubtitles(80, index => $"Readable English dialogue line number {index}.");

        var result = SubtitleOcrQualityAnalyzer.Analyze(
            subtitles,
            minQualityScore: 80,
            allowSparse: false,
            expectedLanguage: "chi");

        Assert.False(result.Accepted);
        Assert.Contains(ScriptMismatchIssue, result.IssueSummary);
        Assert.True(result.QualityScore < 80);
    }

    [Fact]
    public void Analyze_WithLongLatinGibberishForExpectedChinese_RejectsScriptMismatch()
    {
        var subtitles = BuildSubtitles(
            80,
            index => $"Precisionisastonishinglyaccurate blorptastichope {index}");

        var result = SubtitleOcrQualityAnalyzer.Analyze(
            subtitles,
            minQualityScore: 80,
            allowSparse: false,
            expectedLanguage: "zho");

        Assert.False(result.Accepted);
        Assert.Contains(ScriptMismatchIssue, result.IssueSummary);
    }

    [Fact]
    public void Analyze_WithChineseDialogueForExpectedChinese_AcceptsOutput()
    {
        var subtitles = BuildSubtitles(
            80,
            _ => "技能是上古之果在远古时代赐予人类的特殊能力");

        var result = SubtitleOcrQualityAnalyzer.Analyze(
            subtitles,
            minQualityScore: 80,
            allowSparse: false,
            expectedLanguage: "chi");

        Assert.True(result.Accepted);
        Assert.True(result.QualityScore >= 80);
        Assert.DoesNotContain(ScriptMismatchIssue, result.IssueSummary);
    }

    [Fact]
    public void Analyze_WithJapaneseDialogueForExpectedJapanese_AcceptsOutput()
    {
        var subtitles = BuildSubtitles(
            80,
            _ => "これは日本語の字幕です。");

        var result = SubtitleOcrQualityAnalyzer.Analyze(
            subtitles,
            minQualityScore: 80,
            allowSparse: false,
            expectedLanguage: "jpn");

        Assert.True(result.Accepted);
        Assert.True(result.QualityScore >= 80);
    }

    [Fact]
    public void Analyze_WithoutExpectedLanguage_DoesNotApplyCjkScriptGate()
    {
        var subtitles = BuildSubtitles(80, index => $"Readable English dialogue line number {index}.");

        var result = SubtitleOcrQualityAnalyzer.Analyze(
            subtitles,
            minQualityScore: 80,
            allowSparse: false);

        Assert.True(result.Accepted);
        Assert.DoesNotContain(ScriptMismatchIssue, result.IssueSummary);
    }

    private static List<SubtitleItem> BuildSubtitles(int count, Func<int, string> textFactory)
    {
        var result = new List<SubtitleItem>();
        for (var index = 0; index < count; index++)
        {
            var text = textFactory(index);
            result.Add(new SubtitleItem
            {
                Position = index + 1,
                StartTime = index * 1000,
                EndTime = index * 1000 + 800,
                Lines = [text],
                PlaintextLines = [text]
            });
        }

        return result;
    }
}
