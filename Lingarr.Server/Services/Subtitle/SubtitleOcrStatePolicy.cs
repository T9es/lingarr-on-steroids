using Lingarr.Core.Entities;
using Lingarr.Core.Enum;

namespace Lingarr.Server.Services.Subtitle;

internal static class SubtitleOcrStatePolicy
{
    public static readonly TimeSpan TransientStatusTimeout = TimeSpan.FromHours(1);

    public static bool IsTransient(SubtitleOcrStatus status)
    {
        return status is SubtitleOcrStatus.Queued or SubtitleOcrStatus.Processing;
    }

    public static bool IsStaleTransient(EmbeddedSubtitle subtitle, DateTime utcNow)
    {
        if (!IsTransient(subtitle.OcrStatus))
        {
            return false;
        }

        var lastActivity = subtitle.OcrAttemptedAt ?? subtitle.UpdatedAt;
        return lastActivity <= utcNow.Subtract(TransientStatusTimeout);
    }

    public static void ResetStaleTransient(EmbeddedSubtitle subtitle)
    {
        subtitle.OcrStatus = SubtitleOcrStatus.NotStarted;
        subtitle.OcrExtractedPath = null;
        subtitle.OcrError = null;
        subtitle.OcrAttemptedAt = null;
        subtitle.OcrCompletedAt = null;
        subtitle.OcrCueCount = null;
        subtitle.OcrQualityScore = null;
        subtitle.OcrIssueSummary = null;
        subtitle.OcrApprovedAt = null;
    }
}
