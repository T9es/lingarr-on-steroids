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
        Assert.Equal("non-language", plan.Nodes[0].PassThroughReason);
        Assert.Equal(SubtitleTranslationNodeKind.Representative, plan.Nodes[1].Kind);
        Assert.Equal("kokoro ni kakushiteta omoi", plan.Nodes[1].ProviderText);
        Assert.Equal(SubtitleTranslationNodeKind.PassThrough, plan.Nodes[2].Kind);
        Assert.Equal("non-language", plan.Nodes[2].PassThroughReason);
        Assert.Equal(SubtitleTranslationNodeKind.Representative, plan.Nodes[3].Kind);
        Assert.Equal(SubtitleTranslationNodeKind.DuplicateMember, plan.Nodes[4].Kind);
        Assert.Equal(4, plan.Nodes[4].RepresentativePosition);

        Assert.Equal([2, 4], plan.RepresentativeNodes.Select(node => node.Subtitle.Position));
    }
}
