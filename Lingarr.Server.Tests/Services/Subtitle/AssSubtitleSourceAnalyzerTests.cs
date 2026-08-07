using System.Collections.Generic;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class AssSubtitleSourceAnalyzerTests
{
    [Fact]
    public void Analyze_SamStyleSignsDump_FlagsAsPathological()
    {
        var entries = BuildEntries(total: 200_000, positionedCount: 190_000);

        var analysis = AssSubtitleSourceAnalyzer.Analyze(entries);

        Assert.True(analysis.HasSignsDump);
        Assert.True(analysis.HasExplosiveCueCount);
        Assert.True(analysis.IsPathological);
        Assert.Equal(-140, analysis.ContentScoreAdjustment);
    }

    [Fact]
    public void Analyze_BigDialogueTrackWithoutPositionedText_IsNotPathological()
    {
        var entries = BuildEntries(total: 50_000, positionedCount: 0);

        var analysis = AssSubtitleSourceAnalyzer.Analyze(entries);

        Assert.False(analysis.HasSignsDump);
        Assert.False(analysis.HasExplosiveCueCount);
        Assert.False(analysis.IsPathological);
        Assert.Equal(0, analysis.ContentScoreAdjustment);
    }

    [Fact]
    public void Analyze_LegitDialogueTrack_IsNotPathological()
    {
        var entries = BuildEntries(total: 2_000, positionedCount: 100);

        var analysis = AssSubtitleSourceAnalyzer.Analyze(entries);

        Assert.False(analysis.HasSignsDump);
        Assert.False(analysis.HasExplosiveCueCount);
        Assert.False(analysis.IsPathological);
        Assert.Equal(0, analysis.ContentScoreAdjustment);
    }

    [Theory]
    [InlineData(19_999, false)]
    [InlineData(20_000, true)]
    public void Analyze_SignsDumpThresholdBoundary_FlagsExactlyAtTwentyThousandEntries(
        int entryCount,
        bool expectedFlagged)
    {
        var entries = BuildEntries(total: entryCount, positionedCount: (int)(entryCount * 0.95));

        var analysis = AssSubtitleSourceAnalyzer.Analyze(entries);

        Assert.Equal(expectedFlagged, analysis.HasSignsDump);
        Assert.False(analysis.HasExplosiveCueCount);
        Assert.Equal(expectedFlagged, analysis.IsPathological);
        Assert.Equal(expectedFlagged ? -70 : 0, analysis.ContentScoreAdjustment);
    }

    [Fact]
    public void Analyze_ExplosiveCueCountBackstop_FlagsWithoutPositionedText()
    {
        var entries = BuildEntries(total: 150_000, positionedCount: 0);

        var analysis = AssSubtitleSourceAnalyzer.Analyze(entries);

        Assert.False(analysis.HasSignsDump);
        Assert.True(analysis.HasExplosiveCueCount);
        Assert.True(analysis.IsPathological);
        Assert.Equal(-70, analysis.ContentScoreAdjustment);
    }

    [Fact]
    public void Analyze_EmptyEntries_IsNotPathological()
    {
        var analysis = AssSubtitleSourceAnalyzer.Analyze(new List<AssSubtitleSourceAnalysisEntry>());

        Assert.False(analysis.HasSignsDump);
        Assert.False(analysis.HasExplosiveCueCount);
        Assert.False(analysis.IsPathological);
        Assert.Equal(0, analysis.ContentScoreAdjustment);
    }

    [Theory]
    [InlineData("{\\pos(960,540)}Sign")]
    [InlineData("{\\fscx90\\fscy90}Sign")]
    [InlineData("{\\fscx50}Sign")]
    [InlineData("{\\fscy50}Sign")]
    public void CreateEntry_DetectsPositionedTypesettingTags(string line)
    {
        var item = new SubtitleItem
        {
            Lines = [line],
            PlaintextLines = ["Sign"]
        };
        var structure = SubtitleTextStructureFactory.Create(
            item,
            stripSubtitleFormatting: false,
            preserveAssFormatting: false);

        var entry = AssSubtitleSourceAnalyzer.CreateEntry(
            item,
            structure,
            structure.ProviderVisibleText,
            isTranslatable: true,
            rawSourceCharCount: 0);

        Assert.True(entry.HasPositionedTypesetting);
    }

    [Fact]
    public void CreateEntry_PlainDialogue_IsNotPositionedTypesetting()
    {
        var item = new SubtitleItem
        {
            Lines = ["This is ordinary dialogue."],
            PlaintextLines = ["This is ordinary dialogue."]
        };
        var structure = SubtitleTextStructureFactory.Create(
            item,
            stripSubtitleFormatting: false,
            preserveAssFormatting: false);

        var entry = AssSubtitleSourceAnalyzer.CreateEntry(
            item,
            structure,
            structure.ProviderVisibleText,
            isTranslatable: true,
            rawSourceCharCount: 0);

        Assert.False(entry.HasPositionedTypesetting);
    }

    private static List<AssSubtitleSourceAnalysisEntry> BuildEntries(int total, int positionedCount)
    {
        var entries = new List<AssSubtitleSourceAnalysisEntry>(total);
        for (var index = 1; index <= total; index++)
        {
            var positioned = index <= positionedCount;
            entries.Add(new AssSubtitleSourceAnalysisEntry(
                index,
                positioned ? $"Sign text {index}" : $"Dialogue line {index}",
                IsTranslatable: true,
                RawSourceCharCount: 24,
                ProviderVisibleCharCount: 14,
                HasDrawingCommands: false,
                HasPositionedTypesetting: positioned,
                StyleName: "Default"));
        }

        return entries;
    }
}
