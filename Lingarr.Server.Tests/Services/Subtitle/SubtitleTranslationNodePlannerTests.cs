using System.Collections.Generic;
using System.Linq;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SubtitleTranslationNodePlannerTests
{
    [Fact]
    public void Plan_ConservativePolicy_PassesThroughOnlyNonLanguageNodesAndDeduplicatesVisibleText()
    {
        var subtitles = new List<SubtitleItem>
        {
            new()
            {
                Position = 1,
                Lines = ["♪～"],
                PlaintextLines = ["♪～"]
            },
            new()
            {
                Position = 2,
                Lines = ["kokoro ni kakushiteta omoi"],
                PlaintextLines = ["kokoro ni kakushiteta omoi"]
            },
            new()
            {
                Position = 3,
                Lines = ["{\\p1}m 0 0 l 12 12{\\p0}"],
                PlaintextLines = [string.Empty],
                SsaFormat = new SsaFormat { WrapStyle = SsaWrapStyle.None },
                SsaDialogue = new SsaDialogue { Style = "Draw" }
            },
            new()
            {
                Position = 4,
                Lines = ["{\\an7}Fran"],
                PlaintextLines = ["Fran"],
                SsaFormat = new SsaFormat { WrapStyle = SsaWrapStyle.None },
                SsaDialogue = new SsaDialogue { Style = "Signs" }
            },
            new()
            {
                Position = 5,
                Lines = ["{\\an8}Fran"],
                PlaintextLines = ["Fran"],
                SsaFormat = new SsaFormat { WrapStyle = SsaWrapStyle.None },
                SsaDialogue = new SsaDialogue { Style = "Signs" }
            }
        };

        var plan = SubtitleTranslationNodePlanner.Plan(
            subtitles,
            stripSubtitleFormatting: false,
            preserveAssFormatting: true);

        Assert.Equal(5, plan.Nodes.Count);
        Assert.Equal(SubtitleTranslationNodeKind.PassThrough, plan.Nodes[0].Kind);
        Assert.Equal("symbol-only", plan.Nodes[0].PassThroughReason);
        Assert.Equal(SubtitleTranslationNodeKind.Representative, plan.Nodes[1].Kind);
        Assert.Equal("kokoro ni kakushiteta omoi", plan.Nodes[1].ProviderText);
        Assert.Equal(SubtitleTranslationNodeKind.PassThrough, plan.Nodes[2].Kind);
        Assert.Equal("drawing-only", plan.Nodes[2].PassThroughReason);
        Assert.Equal(SubtitleTranslationNodeKind.Representative, plan.Nodes[3].Kind);
        Assert.Equal(SubtitleTranslationNodeKind.DuplicateMember, plan.Nodes[4].Kind);
        Assert.Equal(4, plan.Nodes[4].RepresentativePosition);

        Assert.Equal([2, 4], plan.RepresentativeNodes.Select(node => node.Subtitle.Position));
    }

    [Fact]
    public void Plan_SemanticPolicy_MarksNonDialogueNodesAsProviderOptional()
    {
        var subtitles = new List<SubtitleItem>
        {
            new()
            {
                Position = 1,
                Lines = ["[grumbles softly]"],
                PlaintextLines = ["[grumbles softly]"]
            },
            new()
            {
                Position = 2,
                Lines = ["Rent-a-Girlfriend"],
                PlaintextLines = ["Rent-a-Girlfriend"],
                SsaDialogue = new SsaDialogue { Style = "Title" }
            },
            new()
            {
                Position = 3,
                Lines = ["Noho i ka lipo"],
                PlaintextLines = ["Noho i ka lipo"]
            },
            new()
            {
                Position = 4,
                Lines = ["We need to leave right now."],
                PlaintextLines = ["We need to leave right now."]
            }
        };

        var plan = SubtitleTranslationNodePlanner.Plan(
            subtitles,
            stripSubtitleFormatting: false,
            preserveAssFormatting: true);

        Assert.All(plan.Nodes, node => Assert.Equal(SubtitleTranslationNodeKind.Representative, node.Kind));
        Assert.Equal(SubtitleSemanticKind.SdhSoundEffect, plan.Nodes[0].SemanticKind);
        Assert.Equal(SubtitleSemanticKind.SignOrTitle, plan.Nodes[1].SemanticKind);
        Assert.Equal(SubtitleSemanticKind.LyricOrChant, plan.Nodes[2].SemanticKind);
        Assert.Equal(SubtitleSemanticKind.Dialogue, plan.Nodes[3].SemanticKind);
        Assert.True(plan.Nodes[0].CanPreserveSourceWhenProviderMissing);
        Assert.True(plan.Nodes[1].CanPreserveSourceWhenProviderMissing);
        Assert.True(plan.Nodes[2].CanPreserveSourceWhenProviderMissing);
        Assert.False(plan.Nodes[3].CanPreserveSourceWhenProviderMissing);
    }

    [Fact]
    public void Plan_OcrDamagedDialogue_RemainsTranslatable()
    {
        var subtitles = new List<SubtitleItem>
        {
            new()
            {
                Position = 40,
                Lines = ["- [ANSWER TO NO MAN"],
                PlaintextLines = ["- [ANSWER TO NO MAN"]
            },
            new()
            {
                Position = 291,
                Lines = ["- I'M SORRY, IKK],", "BUT I'M WITH MEELO ON THIS."],
                PlaintextLines = ["- I'M SORRY, IKK],", "BUT I'M WITH MEELO ON THIS."]
            }
        };

        var plan = SubtitleTranslationNodePlanner.Plan(
            subtitles,
            stripSubtitleFormatting: false,
            preserveAssFormatting: false);

        Assert.All(plan.Nodes, node =>
        {
            Assert.Equal(SubtitleTranslationNodeKind.Representative, node.Kind);
            Assert.Equal(SubtitleSemanticKind.CorruptText, node.SemanticKind);
            Assert.True(node.IsTranslatable);
            Assert.False(node.CanPreserveSourceWhenProviderMissing);
            Assert.Null(node.PassThroughReason);
        });
        Assert.Equal([40, 291], plan.RepresentativeNodes.Select(node => node.Subtitle.Position));
    }
}
