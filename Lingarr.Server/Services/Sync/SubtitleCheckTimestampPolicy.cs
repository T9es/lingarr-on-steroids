using Lingarr.Core.Enum;

namespace Lingarr.Server.Services.Sync;

internal static class SubtitleCheckTimestampPolicy
{
    public static bool ShouldStampAfterStateRefresh(TranslationState state)
    {
        return state == TranslationState.AwaitingSource;
    }
}
