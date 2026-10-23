using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Subtitle;

namespace Lingarr.Server.Services.Subtitle;

public static class SubtitleOcrQualityAnalyzer
{
    private const double MaximumEmptyCueRatio = 0.15;
    private const double MaximumCorruptCueRatio = 0.10;
    private const double MinimumCjkLetterRatio = 0.10;
    private const int CjkScriptMismatchPenalty = 45;
    private const string CjkScriptMismatchIssue =
        "OCR output script does not match expected CJK source language.";

    private static readonly HashSet<string> CjkLanguageCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "zh",
        "chi",
        "zho",
        "ja",
        "jpn",
        "jp",
        "ko",
        "kor"
    };

    public static SubtitleOcrQualityResult Analyze(
        IReadOnlyList<SubtitleItem> subtitles,
        int minQualityScore,
        bool allowSparse,
        string? expectedLanguage = null)
    {
        var issues = new List<string>();
        var cueCount = subtitles.Count;
        var meaningfulTexts = subtitles
            .Select(GetText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();
        // Empty-cue detection must use the raw OCR text. GetText projects PlaintextLines,
        // where StripPoisonContent removes bracketed/parenthetical SDH annotations and music
        // glyphs that OCR read correctly; only genuinely blank frames count as empty here.
        var rawTextCueCount = subtitles.Count(subtitle => !string.IsNullOrWhiteSpace(GetRawText(subtitle)));

        var emptyCueRatio = cueCount == 0 ? 1 : (double)(cueCount - rawTextCueCount) / cueCount;
        var corruptCount = meaningfulTexts.Count(SubtitleSemanticClassifier.IsLikelyCorruptText);
        var corruptRatio = meaningfulTexts.Count == 0 ? 0 : (double)corruptCount / meaningfulTexts.Count;
        var health = SubtitleSourceHealthAnalyzer.Analyze(subtitles);
        var scriptMismatch = IsExpectedCjkLanguage(expectedLanguage) &&
                             !HasExpectedCjkScript(meaningfulTexts);

        var qualityScore = 100;
        if (!allowSparse && cueCount < SubtitleExtractionService.MinimumDialogueEntries)
        {
            var penalty = SubtitleExtractionService.MinimumDialogueEntries - cueCount;
            qualityScore -= Math.Min(45, penalty);
            issues.Add($"Only {cueCount} cues were found.");
        }

        if (emptyCueRatio >= MaximumEmptyCueRatio)
        {
            qualityScore -= 25;
            issues.Add($"{emptyCueRatio:P0} of cues are empty.");
        }

        if (corruptRatio >= MaximumCorruptCueRatio)
        {
            qualityScore -= 35;
            issues.Add($"{corruptRatio:P0} of readable cues look corrupt.");
        }

        var sparseOnlyHealthFailure = allowSparse &&
                                      health.Status == SubtitleSourceHealthStatus.Empty &&
                                      cueCount > 0;

        if (!health.IsUsable && !sparseOnlyHealthFailure)
        {
            qualityScore -= health.Status == SubtitleSourceHealthStatus.CorruptText ? 35 : 25;
            issues.Add(health.Reason);
        }

        if (scriptMismatch)
        {
            qualityScore -= CjkScriptMismatchPenalty;
            issues.Add(CjkScriptMismatchIssue);
        }

        qualityScore = Math.Clamp(qualityScore, 0, 100);
        if (scriptMismatch)
        {
            qualityScore = Math.Min(qualityScore, Math.Max(0, minQualityScore - 1));
        }

        var accepted = cueCount > 0 &&
                       (allowSparse || cueCount >= SubtitleExtractionService.MinimumDialogueEntries) &&
                       emptyCueRatio < MaximumEmptyCueRatio &&
                       corruptRatio < MaximumCorruptCueRatio &&
                       (health.IsUsable || sparseOnlyHealthFailure) &&
                       !scriptMismatch &&
                       qualityScore >= minQualityScore;

        if (qualityScore < minQualityScore)
        {
            issues.Add($"Quality score {qualityScore} is below the required {minQualityScore}.");
        }

        return new SubtitleOcrQualityResult
        {
            Accepted = accepted,
            CueCount = cueCount,
            QualityScore = qualityScore,
            IssueSummary = issues.Count == 0 ? "OCR output passed quality checks." : string.Join(" ", issues.Distinct()),
            Subtitles = subtitles.ToList()
        };
    }

    private static bool IsExpectedCjkLanguage(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return false;
        }

        var normalized = SubtitleLanguageHelper.NormalizeLanguageCode(languageCode);
        if (!string.IsNullOrWhiteSpace(normalized) && CjkLanguageCodes.Contains(normalized))
        {
            return true;
        }

        return CjkLanguageCodes.Contains(languageCode.Trim());
    }

    private static bool HasExpectedCjkScript(IReadOnlyList<string> texts)
    {
        var letters = 0;
        var cjkLetters = 0;
        foreach (var text in texts)
        {
            foreach (var character in text)
            {
                if (!char.IsLetter(character))
                {
                    continue;
                }

                letters++;
                if (IsCjkLetter(character))
                {
                    cjkLetters++;
                }
            }
        }

        if (cjkLetters == 0)
        {
            return false;
        }

        return letters > 0 && (double)cjkLetters / letters >= MinimumCjkLetterRatio;
    }

    private static bool IsCjkLetter(char character)
    {
        return character is >= '\u1100' and <= '\u11ff' // Hangul Jamo
            or >= '\u3005' and <= '\u3006'            // CJK iteration and closing marks
            or >= '\u3040' and <= '\u30ff'            // Hiragana and Katakana
            or >= '\u3130' and <= '\u318f'            // Hangul compatibility Jamo
            or >= '\u3400' and <= '\u4dbf'            // CJK Unified Ideographs Extension A
            or >= '\u4e00' and <= '\u9fff'            // CJK Unified Ideographs
            or >= '\uac00' and <= '\ud7af'            // Hangul syllables
            or >= '\uf900' and <= '\ufaff';           // CJK Compatibility Ideographs
    }

    private static string GetText(SubtitleItem subtitle)
    {
        var lines = subtitle.PlaintextLines.Count > 0
            ? subtitle.PlaintextLines
            : subtitle.Lines;
        return string.Join(' ', lines).Trim();
    }

    private static string GetRawText(SubtitleItem subtitle)
    {
        return subtitle.Lines.Count > 0
            ? string.Join(' ', subtitle.Lines).Trim()
            : GetText(subtitle);
    }
}
