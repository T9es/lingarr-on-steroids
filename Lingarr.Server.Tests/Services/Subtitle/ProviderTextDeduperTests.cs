using System.Linq;
using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class ProviderTextDeduperTests
{
    [Fact]
    public void Deduplicate_SameTextWithDifferentSemanticKinds_UsesSeparateRepresentatives()
    {
        var result = ProviderTextDeduper.Deduplicate(
        [
            new ProviderTextItem(1, "ON SCREEN", SubtitleSemanticKind.Dialogue),
            new ProviderTextItem(2, "ON SCREEN", SubtitleSemanticKind.SignOrTitle),
            new ProviderTextItem(3, "ON SCREEN", SubtitleSemanticKind.SignOrTitle),
            new ProviderTextItem(4, "ON SCREEN", SubtitleSemanticKind.LyricOrChant)
        ]);

        Assert.Equal([1, 2, 4], result.Representatives.Select(item => item.Position));
        Assert.Equal([2, 3], result.GetMemberPositions(2));
        Assert.Equal([1], result.GetMemberPositions(1));
        Assert.Equal([4], result.GetMemberPositions(4));
    }
}
