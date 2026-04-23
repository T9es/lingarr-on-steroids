using System.Collections.Generic;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class AssSubtitleArtifactDetectorTests
{
    [Fact]
    public void CompareTagStructure_WithMatchingSrtAssTags_DoesNotFlag()
    {
        var source = new List<SubtitleItem> { Item(1, "{\\an7}Hello") };
        var target = new List<SubtitleItem> { Item(1, "{\\an7}Czesc") };

        var result = AssSubtitleArtifactDetector.CompareTagStructure(source, target, "target.srt");

        Assert.False(result.HasIssues);
    }

    [Fact]
    public void CompareTagStructure_WithUnexpectedTargetAssTags_FlagsUnexpectedTags()
    {
        var source = new List<SubtitleItem> { Item(1, "Hello") };
        var target = new List<SubtitleItem> { Item(1, "{\\an7}Czesc") };

        var result = AssSubtitleArtifactDetector.CompareTagStructure(source, target, "target.srt");

        Assert.True(result.HasIssues);
        Assert.Contains(AssVerificationIssueTypes.UnexpectedAssTags, result.IssueTypes);
        Assert.DoesNotContain(AssVerificationIssueTypes.AssTagMismatch, result.IssueTypes);
    }

    [Fact]
    public void CompareTagStructure_WithDifferentTagSignature_FlagsMismatch()
    {
        var source = new List<SubtitleItem> { Item(1, "{\\pos(1,2)}Hello") };
        var target = new List<SubtitleItem> { Item(1, "{\\pos(999,2)}Czesc") };

        var result = AssSubtitleArtifactDetector.CompareTagStructure(source, target, "target.srt");

        Assert.True(result.HasIssues);
        Assert.Contains(AssVerificationIssueTypes.AssTagMismatch, result.IssueTypes);
        Assert.DoesNotContain(AssVerificationIssueTypes.UnexpectedAssTags, result.IssueTypes);
    }

    [Theory]
    [InlineData("target.ass")]
    [InlineData("target.ssa")]
    public void CompareTagStructure_WithAssTarget_DoesNotCompareTags(string targetPath)
    {
        var source = new List<SubtitleItem> { Item(1, "Hello") };
        var target = new List<SubtitleItem> { Item(1, "{\\an7}Czesc") };

        var result = AssSubtitleArtifactDetector.CompareTagStructure(source, target, targetPath);

        Assert.False(result.HasIssues);
    }

    [Fact]
    public void DetectDrawingArtifacts_WithRepeatedVectorResidue_FlagsDrawingArtifact()
    {
        var result = AssSubtitleArtifactDetector.DetectDrawingArtifacts(
        [
            "m 123 456 l 789 012",
            "m -1.5 0 l 1 1"
        ]);

        Assert.True(result.HasIssues);
        Assert.Equal(2, result.SuspiciousLineCount);
        Assert.Contains(AssVerificationIssueTypes.DrawingArtifact, result.IssueTypes);
    }

    [Fact]
    public void DetectInlineTagPlacementArtifacts_WithItalicTagInsideWords_FlagsInlinePlacement()
    {
        var result = AssSubtitleArtifactDetector.DetectInlineTagPlacementArtifacts(
        [
            @"Moge wpasc pograc w{\i1}Brave{\i0}Star?"
        ]);

        Assert.True(result.HasIssues);
        Assert.Equal(1, result.SuspiciousLineCount);
        Assert.Contains(AssVerificationIssueTypes.InlineAssTagPlacement, result.IssueTypes);
    }

    [Fact]
    public void DetectInlineTagPlacementArtifacts_WithPositionTagsBeforeText_DoesNotFlag()
    {
        var result = AssSubtitleArtifactDetector.DetectInlineTagPlacementArtifacts(
        [
            @"{\an7}{\pos(115,228)}JAK SIĘ WYMAWIA"
        ]);

        Assert.False(result.HasIssues);
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
}
