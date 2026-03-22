using Lingarr.Core.Enum;
using Lingarr.Server.Services.Sync;
using Xunit;

namespace Lingarr.Server.Tests.Services.Sync;

public class SubtitleCheckTimestampPolicyTests
{
    [Theory]
    [InlineData(TranslationState.AwaitingSource, true)]
    [InlineData(TranslationState.Pending, false)]
    [InlineData(TranslationState.InProgress, false)]
    [InlineData(TranslationState.Complete, false)]
    [InlineData(TranslationState.Stale, false)]
    [InlineData(TranslationState.Unknown, false)]
    [InlineData(TranslationState.NotApplicable, false)]
    [InlineData(TranslationState.NoSuitableSubtitles, false)]
    [InlineData(TranslationState.Failed, false)]
    public void ShouldStampAfterStateRefresh_ReturnsExpectedValue(
        TranslationState state,
        bool expected)
    {
        var result = SubtitleCheckTimestampPolicy.ShouldStampAfterStateRefresh(state);

        Assert.Equal(expected, result);
    }
}
