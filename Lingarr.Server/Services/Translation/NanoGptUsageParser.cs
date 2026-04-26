using System.Text.Json;
using Lingarr.Server.Models.NanoGpt;

namespace Lingarr.Server.Services.Translation;

internal static class NanoGptUsageParser
{
    public static NanoGptUsageSnapshot Parse(JsonElement root)
    {
        var snapshot = new NanoGptUsageSnapshot
        {
            Active = TryGetBool(root, "active") ?? false,
            State = TryGetString(root, "state")
        };

        var hasLimits = root.TryGetProperty("limits", out var limits);
        if (hasLimits)
        {
            snapshot.Daily.Limit = TryGetLong(limits, "daily");
            snapshot.Monthly.Limit = TryGetLong(limits, "monthly");
        }

        if (root.TryGetProperty("daily", out var daily))
        {
            ApplyWindow(snapshot.Daily, daily);
        }

        if (TryApplyWindow(
                root,
                hasLimits ? limits : null,
                "dailyImages",
                "dailyImages",
                snapshot.DailyImages))
        {
            // Display-only image quota. It is not used for translation reserve enforcement.
        }

        if (root.TryGetProperty("monthly", out var monthly))
        {
            ApplyWindow(snapshot.Monthly, monthly);
        }

        if (root.TryGetProperty("period", out var period))
        {
            snapshot.CurrentPeriodEnd = TryGetDateTime(period, "currentPeriodEnd");
        }

        snapshot.WeeklyTokens = ExtractTokenWindow(root, hasLimits ? limits : null);

        return snapshot;
    }

    private static NanoGptUsageWindow ExtractTokenWindow(JsonElement root, JsonElement? limits)
    {
        var currentApiWindow = new NanoGptUsageWindow
        {
            Limit = limits.HasValue
                ? TryGetLong(limits.Value, "weeklyInputTokens", "weekly_input_tokens")
                : null
        };
        if (TryApplyWindow(
                root,
                limits,
                "weeklyInputTokens",
                "weeklyInputTokens",
                currentApiWindow))
        {
            return currentApiWindow;
        }

        foreach (var containerName in new[] { "tokenUsage", "tokens", "includedTokens", "included_token_usage" })
        {
            if (!root.TryGetProperty(containerName, out var container) || container.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var windowName in new[] { "weekly", "week", "input", "inputTokens", "input_tokens" })
            {
                if (container.TryGetProperty(windowName, out var window) && window.ValueKind == JsonValueKind.Object)
                {
                    var parsed = new NanoGptUsageWindow();
                    ApplyWindow(parsed, window);
                    parsed.Limit ??= TryGetLong(window, "limit", "allowance", "total");
                    return parsed;
                }
            }

            var fallback = new NanoGptUsageWindow();
            ApplyWindow(fallback, container);
            fallback.Limit ??= TryGetLong(container, "limit", "allowance", "total");
            if (fallback.Limit.HasValue || fallback.Used > 0 || fallback.Remaining.HasValue)
            {
                return fallback;
            }
        }

        return new NanoGptUsageWindow();
    }

    private static bool TryApplyWindow(
        JsonElement root,
        JsonElement? limits,
        string windowName,
        string limitName,
        NanoGptUsageWindow target)
    {
        if (!root.TryGetProperty(windowName, out var window) || window.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (limits.HasValue)
        {
            target.Limit ??= TryGetLong(limits.Value, limitName);
        }

        ApplyWindow(target, window);
        target.Limit ??= TryGetLong(window, "limit", "allowance", "total");
        return true;
    }

    private static void ApplyWindow(NanoGptUsageWindow target, JsonElement source)
    {
        target.Used = TryGetLong(source, "used", "usage", "tokensUsed", "tokens_used") ?? target.Used;
        target.Remaining = TryGetLong(source, "remaining", "tokensRemaining", "tokens_remaining");
        target.Limit = TryGetLong(source, "limit", "allowance", "total") ?? target.Limit;
        target.PercentUsed = TryGetDouble(source, "percentUsed", "percent_used") ?? target.PercentUsed;
        target.ResetAt = TryGetDateTime(source, "resetAt", "reset_at");
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool? TryGetBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }

    private static long? TryGetLong(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var numeric))
            {
                return numeric;
            }

            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static double? TryGetDouble(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var numeric))
            {
                return numeric;
            }

            if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static DateTime? TryGetDateTime(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var milliseconds))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
            }

            if (value.ValueKind == JsonValueKind.String && DateTime.TryParse(value.GetString(), out var parsed))
            {
                return parsed.ToUniversalTime();
            }
        }

        return null;
    }
}
