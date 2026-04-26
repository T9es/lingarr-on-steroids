using Lingarr.Server.Models.NanoGpt;

namespace Lingarr.Server.Services.Translation;

internal static class NanoGptReservePolicy
{
    public static NanoGptReserveResult Evaluate(
        NanoGptUsageSnapshot usage,
        NanoGptReserveSettings settings)
    {
        var reasons = new List<string>();
        DateTime? resetAt = null;

        EvaluateWindow("daily units", usage.Daily, settings.DailyUnitReserve, reasons, ref resetAt);
        EvaluateWindow("monthly units", usage.Monthly, settings.MonthlyUnitReserve, reasons, ref resetAt);
        EvaluateWindow("token reserve", usage.WeeklyTokens, settings.TokenReserve, reasons, ref resetAt);

        return new NanoGptReserveResult
        {
            IsBlocked = reasons.Count > 0,
            Reason = string.Join("; ", reasons),
            ResetAt = resetAt
        };
    }

    private static void EvaluateWindow(
        string label,
        NanoGptUsageWindow window,
        long reserve,
        List<string> reasons,
        ref DateTime? resetAt)
    {
        if (reserve <= 0 || !window.Limit.HasValue)
        {
            return;
        }

        var remaining = window.Remaining ?? Math.Max(window.Limit.Value - window.Used, 0);
        if (remaining > reserve)
        {
            return;
        }

        reasons.Add($"{label} reserve reached ({remaining} remaining, reserve {reserve})");
        if (window.ResetAt.HasValue && (!resetAt.HasValue || window.ResetAt.Value < resetAt.Value))
        {
            resetAt = window.ResetAt;
        }
    }
}
