using Lingarr.Server.Exceptions;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;

namespace Lingarr.Server.Services.Translation;

internal sealed record TranslationLanguageAnalysis(
    int ComparableCount,
    int MismatchedCount,
    double MismatchRatio,
    int ClusterComparableCount,
    int ClusterMismatchedCount,
    double ClusterMismatchRatio,
    IReadOnlyList<int> MismatchedPositions,
    IReadOnlyList<string> Samples,
    string ExpectedDescription,
    string ObservedDescription)
{
    public static TranslationLanguageAnalysis Empty { get; } = new(0, 0, 0, 0, 0, 0, [], [], "", "");

    public bool IsMostlyMismatched => ComparableCount >= 3 && MismatchRatio >= 0.80;
}

internal static class TranslationLanguageGuard
{
    public const string IssueType = "target_language_mismatch";

    private static readonly HashSet<string> EastAsianTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        "ja",
        "jpn",
        "jp",
        "zh",
        "chi",
        "zho",
        "zh-cn",
        "zh-hans",
        "zh-tw",
        "zh-hant",
        "ko",
        "kor"
    };

    public static TranslationLanguageAnalysis AnalyzeBatch(
        IReadOnlyList<BatchSubtitleItem> sourceItems,
        IReadOnlyDictionary<int, string> translatedByPosition,
        string? targetLanguage)
    {
        var target = SubtitleLanguageHelper.NormalizeLanguageCode(targetLanguage);
        if (string.IsNullOrWhiteSpace(target))
        {
            return TranslationLanguageAnalysis.Empty;
        }

        return AnalyzePairs(sourceItems.Select(item =>
        {
            translatedByPosition.TryGetValue(item.Position, out var translated);
            return new TranslationLanguagePair(item.Position, translated);
        }), target);
    }

    public static TranslationLanguageAnalysis AnalyzeSubtitles(
        IReadOnlyList<SubtitleItem> sourceSubtitles,
        IReadOnlyList<SubtitleItem> targetSubtitles,
        string? targetLanguage)
    {
        var target = SubtitleLanguageHelper.NormalizeLanguageCode(targetLanguage);
        if (string.IsNullOrWhiteSpace(target))
        {
            return TranslationLanguageAnalysis.Empty;
        }

        var targetByPosition = targetSubtitles
            .GroupBy(item => item.Position)
            .ToDictionary(group => group.Key, group => group.First());

        return AnalyzePairs(sourceSubtitles.Select(source =>
        {
            targetByPosition.TryGetValue(source.Position, out var targetSubtitle);
            return new TranslationLanguagePair(
                source.Position,
                targetSubtitle == null ? null : GetSubtitleText(targetSubtitle));
        }), target);
    }

    public static void ThrowIfTargetLanguageMismatch(
        IReadOnlyList<BatchSubtitleItem> sourceItems,
        IReadOnlyDictionary<int, string> translatedByPosition,
        string targetLanguage,
        string providerName)
    {
        var analysis = AnalyzeBatch(sourceItems, translatedByPosition, targetLanguage);
        if (!analysis.IsMostlyMismatched)
        {
            return;
        }

        throw new TranslationException(
            $"{providerName} response appears to use the wrong target language. " +
            $"Expected {analysis.ExpectedDescription}, observed {analysis.ObservedDescription}. " +
            $"Mismatched comparable cues: {analysis.MismatchedCount}/{analysis.ComparableCount} ({analysis.MismatchRatio:P0}). " +
            $"Strongest mismatched cluster: {analysis.ClusterMismatchedCount}/{analysis.ClusterComparableCount} ({analysis.ClusterMismatchRatio:P0}).");
    }

    private static TranslationLanguageAnalysis AnalyzePairs(
        IEnumerable<TranslationLanguagePair> pairs,
        string targetLanguage)
    {
        var expectsEastAsian = EastAsianTargets.Contains(targetLanguage);
        var comparableCount = 0;
        var mismatchedPositions = new List<int>();
        var samples = new List<string>();
        var comparableFlags = new List<bool>();

        foreach (var pair in pairs)
        {
            if (string.IsNullOrWhiteSpace(pair.TargetText) || IsBracketOnlyCue(pair.TargetText))
            {
                continue;
            }

            var stats = TextScriptStats.FromText(pair.TargetText);
            if (!stats.IsComparable)
            {
                continue;
            }

            comparableCount++;
            var isMismatch = stats.IsMojibakeLikely ||
                (expectsEastAsian
                ? stats.IsLatinDominant
                : stats.IsEastAsianDominant);
            comparableFlags.Add(isMismatch);
            if (!isMismatch)
            {
                continue;
            }

            mismatchedPositions.Add(pair.Position);
            if (samples.Count < 5)
            {
                samples.Add(pair.TargetText.Trim());
            }
        }

        var mismatchRatio = comparableCount == 0
            ? 0
            : (double)mismatchedPositions.Count / comparableCount;
        var cluster = FindStrongestCluster(comparableFlags, 8);

        return new TranslationLanguageAnalysis(
            comparableCount,
            mismatchedPositions.Count,
            mismatchRatio,
            cluster.WindowSize,
            cluster.MatchedCount,
            cluster.Ratio,
            mismatchedPositions,
            samples,
            expectsEastAsian ? "East Asian-script target text" : "non-East-Asian target text",
            expectsEastAsian ? "mostly Latin-script or damaged text" : "mostly East Asian-script or damaged text");
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

    private static bool IsBracketOnlyCue(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length >= 2 &&
               ((trimmed[0] == '[' && trimmed[^1] == ']') ||
                (trimmed[0] == '(' && trimmed[^1] == ')'));
    }

    private static string GetSubtitleText(SubtitleItem subtitle)
    {
        var lines = subtitle.PlaintextLines.Count > 0
            ? subtitle.PlaintextLines
            : subtitle.Lines;
        return string.Join('\n', lines);
    }

    private sealed record TranslationLanguagePair(int Position, string? TargetText);

    private sealed record TextScriptStats(
        int LetterCount,
        int VisibleCharacterCount,
        int EastAsianLetters,
        int LatinLetters,
        int MojibakeMarkers)
    {
        public bool IsComparable => LetterCount >= 6;

        public bool IsEastAsianDominant =>
            EastAsianLetters >= 4 && (double)EastAsianLetters / LetterCount >= 0.4;

        public bool IsLatinDominant =>
            LatinLetters >= 6 && EastAsianLetters == 0 && (double)LatinLetters / LetterCount >= 0.85;

        public bool IsMojibakeLikely =>
            MojibakeMarkers >= 3 && VisibleCharacterCount > 0 &&
            (double)MojibakeMarkers / VisibleCharacterCount >= 0.08;

        public static TextScriptStats FromText(string text)
        {
            var letterCount = 0;
            var visibleCharacterCount = 0;
            var eastAsianLetters = 0;
            var latinLetters = 0;

            foreach (var c in text)
            {
                if (!char.IsWhiteSpace(c))
                {
                    visibleCharacterCount++;
                }

                if (!char.IsLetter(c))
                {
                    continue;
                }

                letterCount++;
                if (IsEastAsian(c))
                {
                    eastAsianLetters++;
                }
                else if (IsLatin(c))
                {
                    latinLetters++;
                }
            }

            return new TextScriptStats(
                letterCount,
                visibleCharacterCount,
                eastAsianLetters,
                latinLetters,
                CountMojibakeMarkers(text));
        }

        /// <summary>
        /// Counts only actual mis-decoding evidence. A UTF-8 sequence that was decoded as
        /// Latin-1, Windows-125x or Latin-2 shows up as a lead character followed by a
        /// rendered continuation character. The lead byte and the continuation byte are both
        /// damaged, so a matched pair counts twice; standalone replacement characters and C1
        /// controls count once. Typographic quotes, dashes, ellipses and ordinary accented
        /// letters are valid text and are never counted on their own.
        /// </summary>
        private static int CountMojibakeMarkers(string text)
        {
            var markers = 0;
            for (var index = 0; index < text.Length; index++)
            {
                var c = text[index];
                if (index + 1 < text.Length &&
                    IsMojibakeLeadCharacter(c) &&
                    IsMojibakeContinuationCharacter(text[index + 1]))
                {
                    markers += 2;
                    index++;
                    continue;
                }

                if (c == '\uFFFD' || IsInRange(c, '\u0080', '\u009F'))
                {
                    markers++;
                }
            }

            return markers;
        }

        private static bool IsMojibakeLeadCharacter(char c)
        {
            return MojibakeLeadCharacters.Contains(c);
        }

        // UTF-8 lead bytes C2-DF rendered by Latin-1, Windows-1250 and Latin-2. The Latin-1
        // range covers Â-ß; the additional characters are the Latin-2/Windows-1250
        // renderings of the same bytes (for example C5 -> Ĺ instead of Å).
        private static readonly string MojibakeLeadCharacters =
            "ÂÃÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖ×ØÙÚÛÜÝÞßĂĹĆČĘĚĎĐŃŇŐŘŢŤŮŰ";

        private static bool IsMojibakeContinuationCharacter(char c)
        {
            // A UTF-8 continuation byte keeps its value when decoded, so any byte in
            // 0x80-0xBF is evidence. Latin-1 renders 0x80-0x9F as C1 controls and
            // 0xA0-0xBF as printable punctuation and accented capitals.
            if (c == '\uFFFD' ||
                IsInRange(c, '\u0080', '\u009F') ||
                IsInRange(c, '\u00A0', '\u00BF'))
            {
                return true;
            }

            // Windows-1252 and Windows-1250 renderings of bytes 0x80-0x9F, plus the
            // Latin-2/Windows-1250 renderings of bytes 0xA0-0xBF (for example 0xBC -> Ľ).
            return "€‚ƒ„…†‡ˆ‰Š‹ŒŽ‘’“”•–—˜™š›œžŸťŤĄ˘ŁĽŚŞŹŽŻą˛łľśşťźžżˇ˝".Contains(c);
        }

        private static bool IsEastAsian(char c)
        {
            return IsInRange(c, '\u3040', '\u30FF') ||
                   IsInRange(c, '\u3400', '\u4DBF') ||
                   IsInRange(c, '\u4E00', '\u9FFF') ||
                   IsInRange(c, '\uAC00', '\uD7AF');
        }

        private static bool IsLatin(char c)
        {
            return IsInRange(c, 'A', 'Z') ||
                   IsInRange(c, 'a', 'z') ||
                   IsInRange(c, '\u00C0', '\u024F') ||
                   IsInRange(c, '\u1E00', '\u1EFF');
        }

        private static bool IsInRange(char c, char start, char end)
        {
            return c >= start && c <= end;
        }
    }
}
