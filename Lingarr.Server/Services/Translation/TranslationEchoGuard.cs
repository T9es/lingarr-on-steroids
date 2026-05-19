using System.Text;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;

namespace Lingarr.Server.Services.Translation;

internal sealed record TranslationEchoAnalysis(
    int ComparableCount,
    int EchoedCount,
    double EchoRatio,
    int ClusterComparableCount,
    int ClusterEchoedCount,
    double ClusterEchoRatio,
    IReadOnlyList<int> EchoedPositions,
    IReadOnlyList<string> Samples)
{
    public static TranslationEchoAnalysis Empty { get; } = new(0, 0, 0, 0, 0, 0, [], []);

    public bool IsMostlyEchoed => ComparableCount >= 3 && EchoRatio >= 0.80;
}

internal static class TranslationEchoGuard
{
    public const string IssueType = "unchanged_source_text";

    public static TranslationEchoAnalysis AnalyzeBatch(
        IReadOnlyList<BatchSubtitleItem> sourceItems,
        IReadOnlyDictionary<int, string> translatedByPosition,
        string? sourceLanguage,
        string? targetLanguage)
    {
        if (IsSameLanguage(sourceLanguage, targetLanguage))
        {
            return TranslationEchoAnalysis.Empty;
        }

        return AnalyzePairs(sourceItems.Select(item =>
        {
            translatedByPosition.TryGetValue(item.Position, out var translated);
            return new TranslationEchoPair(item.Position, item.Line, translated);
        }));
    }

    public static TranslationEchoAnalysis AnalyzeSubtitles(
        IReadOnlyList<SubtitleItem> sourceSubtitles,
        IReadOnlyList<SubtitleItem> targetSubtitles,
        string? sourceLanguage,
        string? targetLanguage)
    {
        if (IsSameLanguage(sourceLanguage, targetLanguage))
        {
            return TranslationEchoAnalysis.Empty;
        }

        var targetByPosition = targetSubtitles
            .GroupBy(item => item.Position)
            .ToDictionary(group => group.Key, group => group.First());

        return AnalyzePairs(sourceSubtitles.Select(source =>
        {
            targetByPosition.TryGetValue(source.Position, out var target);
            return new TranslationEchoPair(
                source.Position,
                GetSubtitleText(source),
                target == null ? null : GetSubtitleText(target));
        }));
    }

    public static void ThrowIfMostlyEchoed(
        IReadOnlyList<BatchSubtitleItem> sourceItems,
        IReadOnlyDictionary<int, string> translatedByPosition,
        string sourceLanguage,
        string targetLanguage,
        string providerName)
    {
        var analysis = AnalyzeBatch(sourceItems, translatedByPosition, sourceLanguage, targetLanguage);
        if (!analysis.IsMostlyEchoed)
        {
            return;
        }

        throw new TranslationException(
            $"{providerName} response appears to echo the source text instead of translating it. " +
            $"Unchanged comparable cues: {analysis.EchoedCount}/{analysis.ComparableCount} ({analysis.EchoRatio:P0}). " +
            $"Strongest unchanged cluster: {analysis.ClusterEchoedCount}/{analysis.ClusterComparableCount} ({analysis.ClusterEchoRatio:P0}).");
    }

    private static TranslationEchoAnalysis AnalyzePairs(IEnumerable<TranslationEchoPair> pairs)
    {
        var comparableCount = 0;
        var echoedPositions = new List<int>();
        var samples = new List<string>();
        var comparableFlags = new List<bool>();

        foreach (var pair in pairs)
        {
            if (string.IsNullOrWhiteSpace(pair.TargetText) ||
                !TryNormalizeComparable(pair.SourceText, out var sourceComparable) ||
                !TryNormalizeComparable(pair.TargetText, out var targetComparable))
            {
                continue;
            }

            comparableCount++;
            var isEchoed = string.Equals(sourceComparable, targetComparable, StringComparison.Ordinal);
            comparableFlags.Add(isEchoed);
            if (!isEchoed)
            {
                continue;
            }

            echoedPositions.Add(pair.Position);
            if (samples.Count < 5)
            {
                samples.Add(pair.SourceText.Trim());
            }
        }

        var echoRatio = comparableCount == 0
            ? 0
            : (double)echoedPositions.Count / comparableCount;
        var cluster = FindStrongestCluster(comparableFlags, 8);

        return new TranslationEchoAnalysis(
            comparableCount,
            echoedPositions.Count,
            echoRatio,
            cluster.WindowSize,
            cluster.MatchedCount,
            cluster.Ratio,
            echoedPositions,
            samples);
    }

    private static (int WindowSize, int MatchedCount, double Ratio) FindStrongestCluster(
        IReadOnlyList<bool> comparableFlags,
        int windowSize)
    {
        if (comparableFlags.Count < windowSize)
        {
            return (0, 0, 0);
        }

        var current = 0;
        for (var i = 0; i < windowSize; i++)
        {
            if (comparableFlags[i])
            {
                current++;
            }
        }

        var best = current;
        for (var i = windowSize; i < comparableFlags.Count; i++)
        {
            if (comparableFlags[i - windowSize])
            {
                current--;
            }

            if (comparableFlags[i])
            {
                current++;
            }

            best = Math.Max(best, current);
        }

        return (windowSize, best, (double)best / windowSize);
    }

    private static bool TryNormalizeComparable(string? text, out string comparable)
    {
        comparable = string.Empty;
        if (string.IsNullOrWhiteSpace(text) ||
            IsBracketOnlyCue(text) ||
            SubtitleSemanticClassifier.CanIgnoreUnchangedEcho(text))
        {
            return false;
        }

        var normalized = NormalizeForComparison(text);
        if (normalized.Length < 12)
        {
            return false;
        }

        var wordCount = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount < 3)
        {
            return false;
        }

        comparable = normalized;
        return true;
    }

    private static string NormalizeForComparison(string text)
    {
        var normalized = SubtitleTextStructure.NormalizeProviderTranslationText(text)
            .Replace("\\N", " ", StringComparison.Ordinal)
            .Replace("\\n", " ", StringComparison.Ordinal);
        var builder = new StringBuilder(normalized.Length);
        var insideAssTag = false;
        var previousWasSpace = true;

        foreach (var c in normalized)
        {
            if (c == '{')
            {
                insideAssTag = true;
                continue;
            }

            if (insideAssTag)
            {
                if (c == '}')
                {
                    insideAssTag = false;
                }

                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                previousWasSpace = false;
                continue;
            }

            if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    private static bool IsBracketOnlyCue(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length >= 2 &&
               ((trimmed[0] == '[' && trimmed[^1] == ']') ||
                (trimmed[0] == '(' && trimmed[^1] == ')'));
    }

    private static bool IsSameLanguage(string? sourceLanguage, string? targetLanguage)
    {
        var source = SubtitleLanguageHelper.NormalizeLanguageCode(sourceLanguage);
        var target = SubtitleLanguageHelper.NormalizeLanguageCode(targetLanguage);
        return !string.IsNullOrWhiteSpace(source) &&
               !string.IsNullOrWhiteSpace(target) &&
               string.Equals(source, target, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSubtitleText(SubtitleItem subtitle)
    {
        var lines = subtitle.PlaintextLines.Count > 0
            ? subtitle.PlaintextLines
            : subtitle.Lines;
        return string.Join('\n', lines);
    }

    private sealed record TranslationEchoPair(int Position, string SourceText, string? TargetText);
}
