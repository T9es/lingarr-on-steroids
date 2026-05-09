using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Subtitle;

internal enum SubtitleSourceHealthStatus
{
    Healthy,
    Empty,
    CorruptText
}

internal sealed record SubtitleSourceHealthAnalysis(
    SubtitleSourceHealthStatus Status,
    int TotalEntries,
    int MeaningfulEntries,
    int CorruptEntries,
    string Reason)
{
    public bool IsUsable => Status == SubtitleSourceHealthStatus.Healthy;
}

internal static class SubtitleSourceHealthAnalyzer
{
    private const int MinimumMeaningfulEntries = 50;
    private const int MinimumCorruptEntries = 10;
    private const double MinimumCorruptRatio = 0.10;

    public static SubtitleSourceHealthAnalysis Analyze(IReadOnlyList<SubtitleItem> subtitles)
    {
        var meaningfulEntries = subtitles
            .Select(GetText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();
        if (meaningfulEntries.Count < MinimumMeaningfulEntries)
        {
            return new SubtitleSourceHealthAnalysis(
                SubtitleSourceHealthStatus.Empty,
                subtitles.Count,
                meaningfulEntries.Count,
                0,
                $"Only {meaningfulEntries.Count} readable text entries were found.");
        }

        var corruptEntries = meaningfulEntries.Count(SubtitleSemanticClassifier.IsLikelyCorruptText);
        var corruptRatio = (double)corruptEntries / meaningfulEntries.Count;
        if (corruptEntries >= MinimumCorruptEntries && corruptRatio >= MinimumCorruptRatio)
        {
            return new SubtitleSourceHealthAnalysis(
                SubtitleSourceHealthStatus.CorruptText,
                subtitles.Count,
                meaningfulEntries.Count,
                corruptEntries,
                $"Detected {corruptEntries}/{meaningfulEntries.Count} likely OCR or random-text entries.");
        }

        return new SubtitleSourceHealthAnalysis(
            SubtitleSourceHealthStatus.Healthy,
            subtitles.Count,
            meaningfulEntries.Count,
            corruptEntries,
            "Readable subtitle source.");
    }

    private static string GetText(SubtitleItem subtitle)
    {
        var lines = subtitle.PlaintextLines.Count > 0
            ? subtitle.PlaintextLines
            : subtitle.Lines;
        return string.Join(' ', lines).Trim();
    }
}
