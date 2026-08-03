using System.Text;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Subtitle;

public sealed record ExternalSubtitleSourceSelection(Subtitles Subtitle, string SourceLanguage);

public static class ExternalSubtitleCandidateHelper
{
    public static ExternalSubtitleSourceSelection? SelectPrimarySourceCandidate(
        IEnumerable<Subtitles> subtitles,
        IEnumerable<string> configuredSourceLanguages,
        bool ignoreCaptions)
    {
        var validSubtitles = subtitles
            .Where(s => !ShouldSkipAsPrimarySource(s))
            .Where(s => !IsSupplementalOrCommentary(s))
            .ToList();
        if (validSubtitles.Count == 0)
        {
            return null;
        }

        foreach (var sourceLanguage in configuredSourceLanguages
                     .Select(SubtitleLanguageHelper.NormalizeLanguageCode)
                     .Where(language => !string.IsNullOrWhiteSpace(language))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var sourceCandidates = validSubtitles
                .Where(s => SubtitleLanguageHelper.LanguageMatches(s.Language, sourceLanguage))
                .ToList();
            if (sourceCandidates.Count == 0)
            {
                continue;
            }

            var selectedCandidate = OrderPrimarySourceCandidates(sourceCandidates, ignoreCaptions)
                .FirstOrDefault();
            if (selectedCandidate != null)
            {
                return new ExternalSubtitleSourceSelection(selectedCandidate, sourceLanguage);
            }
        }

        return null;
    }

    /// <summary>
    /// Orders candidates by source quality tier: clean full dialogue, clean captions, then pathological ASS fallback.
    /// </summary>
    public static IReadOnlyList<Subtitles> OrderPrimarySourceCandidates(
        IEnumerable<Subtitles> subtitles,
        bool ignoreCaptions)
    {
        var validSubtitles = subtitles
            .Where(s => !ShouldSkipAsPrimarySource(s))
            .Where(s => !IsSupplementalOrCommentary(s))
            .ToList();

        var cleanCandidates = OrderCandidates(
            validSubtitles
                .Where(s => !IsPathologicalAssSource(s))
                .Where(s => !SubtitleLanguageHelper.IsCaptionSubtitleType(GetSubtitleType(s))));
        var captionCandidates = OrderCandidates(
            validSubtitles
                .Where(s => !IsPathologicalAssSource(s))
                .Where(s => SubtitleLanguageHelper.IsCaptionSubtitleType(GetSubtitleType(s))));
        var pathologicalCandidates = OrderCandidates(
            validSubtitles
                .Where(IsPathologicalAssSource)
                .Where(s => !ignoreCaptions ||
                            !SubtitleLanguageHelper.IsCaptionSubtitleType(GetSubtitleType(s))));

        return cleanCandidates
            .Concat(ignoreCaptions ? Enumerable.Empty<Subtitles>() : captionCandidates)
            .Concat(pathologicalCandidates)
            .ToList();
    }

    public static bool ShouldSkipAsPrimarySource(Subtitles subtitle)
    {
        return IsTemporarySource(subtitle) ||
               IsLingarrExtractedArtifact(subtitle) ||
               IsSparseSubtitleFile(subtitle) ||
               IsCorruptTextSubtitleFile(subtitle);
    }

    public static bool ShouldSkipAsMainTarget(Subtitles subtitle)
    {
        return IsTemporarySource(subtitle) ||
               IsLingarrExtractedArtifact(subtitle) ||
               IsSparseSubtitleFile(subtitle) ||
               IsSupplementalOrCommentary(subtitle);
    }

    public static bool IsSupplementalOrCommentary(Subtitles subtitle)
    {
        var subtitleType = GetSubtitleType(subtitle);
        return SubtitleLanguageHelper.IsSupplementalSubtitleType(subtitleType) ||
               string.Equals(
                   subtitleType,
                   SubtitleLanguageHelper.TypeCommentary,
                   StringComparison.OrdinalIgnoreCase);
    }

    public static string GetSubtitleType(Subtitles subtitle)
    {
        if (!string.IsNullOrWhiteSpace(subtitle.Caption))
        {
            var captionType = SubtitleLanguageHelper.DetermineSubtitleTypeFromFilename(subtitle.Caption);
            if (!string.Equals(captionType, SubtitleLanguageHelper.TypeFull, StringComparison.OrdinalIgnoreCase))
            {
                return captionType;
            }
        }

        return SubtitleLanguageHelper.DetermineSubtitleTypeFromFilename(
            !string.IsNullOrWhiteSpace(subtitle.Path) ? subtitle.Path : subtitle.FileName);
    }

    public static bool IsSparseSubtitleFile(Subtitles subtitle)
    {
        if (string.IsNullOrWhiteSpace(subtitle.Path) || !File.Exists(subtitle.Path))
        {
            return false;
        }

        try
        {
            return SubtitleExtractionService.IsSparseSubtitle(subtitle.Path);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsLingarrExtractedArtifact(Subtitles subtitle)
    {
        if (string.IsNullOrWhiteSpace(subtitle.Path) || !File.Exists(subtitle.Path))
        {
            return false;
        }

        try
        {
            return SubtitleExtractionService.IsLingarrExtracted(subtitle.Path);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsPathologicalAssSource(Subtitles subtitle)
    {
        return AnalyzeAssSource(subtitle)?.IsPathological == true;
    }

    private static AssSubtitleSourceAnalysis? AnalyzeAssSource(Subtitles subtitle)
    {
        if (string.IsNullOrWhiteSpace(subtitle.Path) || !File.Exists(subtitle.Path))
        {
            return null;
        }

        var normalizedFormat = SubtitleOutputModeHelper.NormalizeFormat(
            !string.IsNullOrWhiteSpace(subtitle.Format)
                ? subtitle.Format
                : Path.GetExtension(subtitle.Path));
        if (!SubtitleOutputModeHelper.IsAssFormat(normalizedFormat))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(subtitle.Path);
            var subtitles = new SsaParser().ParseStream(stream, Encoding.UTF8);
            return AssSubtitleSourceAnalyzer.AnalyzeSubtitleItems(subtitles);
        }
        catch
        {
            return null;
        }
    }

    public static bool IsCorruptTextSubtitleFile(Subtitles subtitle)
    {
        if (string.IsNullOrWhiteSpace(subtitle.Path) || !File.Exists(subtitle.Path))
        {
            return false;
        }

        try
        {
            var items = File.ReadLines(subtitle.Path)
                .Select(ExtractReadablePayload)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select((line, index) => new SubtitleItem
                {
                    Position = index + 1,
                    Lines = [line],
                    PlaintextLines = [line]
                })
                .Take(5_000)
                .ToList();
            return items.Count > 0 &&
                   SubtitleSourceHealthAnalyzer.Analyze(items).Status == SubtitleSourceHealthStatus.CorruptText;
        }
        catch
        {
            return false;
        }
    }

    public static int ScorePrimarySourceCandidate(Subtitles subtitle)
    {
        var score = 0;
        var normalizedFormat = SubtitleOutputModeHelper.NormalizeFormat(
            !string.IsNullOrWhiteSpace(subtitle.Format)
                ? subtitle.Format
                : Path.GetExtension(subtitle.Path));
        if (normalizedFormat is ".srt" or ".vtt")
        {
            score += 20;
        }
        else if (normalizedFormat is ".ass" or ".ssa")
        {
            score -= 10;
        }

        if (!string.IsNullOrWhiteSpace(subtitle.Caption))
        {
            score -= 5;
        }

        var entryCount = CountEntriesOrNull(subtitle);
        if (entryCount.HasValue)
        {
            score += Math.Min(entryCount.Value, 2_000) / 25;
        }

        score += GetPathologicalAssScoreAdjustment(subtitle);

        return score;
    }

    private static int GetPathologicalAssScoreAdjustment(Subtitles subtitle)
    {
        return AnalyzeAssSource(subtitle)?.ContentScoreAdjustment ?? 0;
    }

    private static IEnumerable<Subtitles> OrderCandidates(IEnumerable<Subtitles> subtitles)
    {
        return subtitles
            .OrderByDescending(ScorePrimarySourceCandidate)
            .ThenBy(s => s.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.FileName, StringComparer.OrdinalIgnoreCase);
    }

    private static string ExtractReadablePayload(string line)
    {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) ||
            trimmed.All(char.IsDigit) ||
            trimmed.Contains("-->", StringComparison.Ordinal) ||
            trimmed.StartsWith("[", StringComparison.Ordinal) ||
            trimmed.StartsWith("Format:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Style:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Comment:", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (!trimmed.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var commaCount = 0;
        for (var index = 0; index < trimmed.Length; index++)
        {
            if (trimmed[index] != ',')
            {
                continue;
            }

            commaCount++;
            if (commaCount == 9 && index + 1 < trimmed.Length)
            {
                return trimmed[(index + 1)..];
            }
        }

        return string.Empty;
    }

    private static int? CountEntriesOrNull(Subtitles subtitle)
    {
        if (string.IsNullOrWhiteSpace(subtitle.Path) || !File.Exists(subtitle.Path))
        {
            return null;
        }

        try
        {
            return SubtitleExtractionService.CountSubtitleEntries(subtitle.Path);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsTemporarySource(Subtitles subtitle)
    {
        var pathOrName = !string.IsNullOrWhiteSpace(subtitle.Path) ? subtitle.Path : subtitle.FileName;
        var fileName = GetFileName(pathOrName);
        return fileName.StartsWith("lingarr_temp_source_", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetFileName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.TrimEnd('/', '\\');
        var separatorIndex = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
        return separatorIndex >= 0 ? trimmed[(separatorIndex + 1)..] : trimmed;
    }
}
