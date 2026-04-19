namespace Lingarr.Server.Services.Subtitle;

public enum SubtitleOutputMode
{
    MatchSource,
    AssOnly,
    SrtOnly,
    Both
}

public static class SubtitleOutputModeHelper
{
    private const string DefaultPlainTextFormat = ".srt";

    public static SubtitleOutputMode Parse(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "ass-only" => SubtitleOutputMode.AssOnly,
            "srt-only" => SubtitleOutputMode.SrtOnly,
            "both" => SubtitleOutputMode.Both,
            _ => SubtitleOutputMode.MatchSource
        };
    }

    public static string ToSettingValue(this SubtitleOutputMode outputMode)
    {
        return outputMode switch
        {
            SubtitleOutputMode.AssOnly => "ass-only",
            SubtitleOutputMode.SrtOnly => "srt-only",
            SubtitleOutputMode.Both => "both",
            _ => "match-source"
        };
    }

    public static IReadOnlyList<string> GetRequiredOutputFormats(string? sourceFormat, SubtitleOutputMode outputMode)
    {
        var normalizedSourceFormat = NormalizeFormat(sourceFormat);
        if (string.IsNullOrEmpty(normalizedSourceFormat))
        {
            normalizedSourceFormat = DefaultPlainTextFormat;
        }

        if (!IsAssFormat(normalizedSourceFormat))
        {
            return outputMode switch
            {
                SubtitleOutputMode.SrtOnly => [DefaultPlainTextFormat],
                _ => [normalizedSourceFormat]
            };
        }

        return outputMode switch
        {
            SubtitleOutputMode.SrtOnly => [DefaultPlainTextFormat],
            SubtitleOutputMode.Both => [normalizedSourceFormat, DefaultPlainTextFormat],
            _ => [normalizedSourceFormat]
        };
    }

    public static bool IsAssFormat(string? format)
    {
        var normalized = NormalizeFormat(format);
        return normalized is ".ass" or ".ssa";
    }

    public static string NormalizeFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return string.Empty;
        }

        var normalized = format.Trim().ToLowerInvariant();
        return normalized.StartsWith('.') ? normalized : $".{normalized}";
    }

    public static string SerializeFormats(IEnumerable<string> formats)
    {
        return string.Join(
            ',',
            formats
                .Select(NormalizeFormat)
                .Where(static format => !string.IsNullOrWhiteSpace(format))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<string> DeserializeFormats(string? serializedFormats)
    {
        if (string.IsNullOrWhiteSpace(serializedFormats))
        {
            return [];
        }

        return serializedFormats
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeFormat)
            .Where(static format => !string.IsNullOrWhiteSpace(format))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
