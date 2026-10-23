using System.Collections.Generic;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SubtitleOcrQualityAnalyzerSdhTests
{
    [Fact]
    public void Analyze_WithSdhAnnotationCues_AcceptsOutput()
    {
        var subtitles = BuildSdhSubtitles();

        var result = SubtitleOcrQualityAnalyzer.Analyze(subtitles, minQualityScore: 70, allowSparse: false);

        Assert.True(result.Accepted);
        Assert.Equal(100, result.CueCount);
        Assert.True(result.QualityScore >= 70);
        Assert.DoesNotContain("of cues are empty", result.IssueSummary);
    }

    [Fact]
    public void Analyze_WithGenuinelyBlankCues_StillRejectsEmptyRatio()
    {
        var subtitles = BuildSubtitlesWithBlankCues(blankCueCount: 20);

        var result = SubtitleOcrQualityAnalyzer.Analyze(subtitles, minQualityScore: 70, allowSparse: false);

        Assert.False(result.Accepted);
        Assert.Contains("of cues are empty", result.IssueSummary);
        Assert.Equal(75, result.QualityScore);
    }

    [Fact]
    public void Analyze_WithFifteenPercentBlankCues_RejectsAtThresholdBoundary()
    {
        var subtitles = BuildSubtitlesWithBlankCues(blankCueCount: 15);

        var result = SubtitleOcrQualityAnalyzer.Analyze(subtitles, minQualityScore: 70, allowSparse: false);

        Assert.False(result.Accepted);
        Assert.Contains("of cues are empty", result.IssueSummary);
    }

    private static List<SubtitleItem> BuildSdhSubtitles()
    {
        var subtitles = new List<SubtitleItem>();
        for (var index = 0; index < 80; index++)
        {
            var text = $"Readable dialogue line {index}.";
            subtitles.Add(CreateCue(index, text, text));
        }

        var annotations = new[]
        {
            ("[dramatic music playing]", 10),
            ("(SIGHS)", 6),
            ("\u266a \u266a", 4)
        };
        var position = 80;
        foreach (var (annotation, count) in annotations)
        {
            for (var index = 0; index < count; index++)
            {
                subtitles.Add(CreateCue(position++, annotation, string.Empty));
            }
        }

        return subtitles;
    }

    private static List<SubtitleItem> BuildSubtitlesWithBlankCues(int blankCueCount)
    {
        var subtitles = new List<SubtitleItem>();
        var dialogueCueCount = 100 - blankCueCount;
        for (var index = 0; index < dialogueCueCount; index++)
        {
            var text = $"Readable dialogue line {index}.";
            subtitles.Add(CreateCue(index, text, text));
        }

        for (var index = dialogueCueCount; index < 100; index++)
        {
            subtitles.Add(CreateCue(index, string.Empty, string.Empty));
        }

        return subtitles;
    }

    private static SubtitleItem CreateCue(int index, string rawText, string plaintext)
    {
        return new SubtitleItem
        {
            Position = index + 1,
            StartTime = index * 1000,
            EndTime = index * 1000 + 800,
            Lines = [rawText],
            PlaintextLines = [plaintext]
        };
    }
}
