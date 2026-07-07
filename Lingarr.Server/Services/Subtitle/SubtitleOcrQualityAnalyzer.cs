using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Subtitle;

namespace Lingarr.Server.Services.Subtitle;

public static class SubtitleOcrQualityAnalyzer
{
    private const double MaximumEmptyCueRatio = 0.15;
    private const double MaximumCorruptCueRatio = 0.10;

    public static SubtitleOcrQualityResult Analyze(
        IReadOnlyList<SubtitleItem> subtitles,
        int minQualityScore,
        bool allowSparse)
    {
        var issues = new List<string>();
        var cueCount = subtitles.Count;
        var meaningfulTexts = subtitles
            .Select(GetText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        var emptyCueRatio = cueCount == 0 ? 1 : (double)(cueCount - meaningfulTexts.Count) / cueCount;
        var corruptCount = meaningfulTexts.Count(SubtitleSemanticClassifier.IsLikelyCorruptText);
        var corruptRatio = meaningfulTexts.Count == 0 ? 0 : (double)corruptCount / meaningfulTexts.Count;
        var health = SubtitleSourceHealthAnalyzer.Analyze(subtitles);

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

        qualityScore = Math.Clamp(qualityScore, 0, 100);

        var accepted = cueCount > 0 &&
                       (allowSparse || cueCount >= SubtitleExtractionService.MinimumDialogueEntries) &&
                       emptyCueRatio < MaximumEmptyCueRatio &&
                       corruptRatio < MaximumCorruptCueRatio &&
                       (health.IsUsable || sparseOnlyHealthFailure) &&
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

    private static string GetText(SubtitleItem subtitle)
    {
        var lines = subtitle.PlaintextLines.Count > 0
            ? subtitle.PlaintextLines
            : subtitle.Lines;
        return string.Join(' ', lines).Trim();
    }
}
