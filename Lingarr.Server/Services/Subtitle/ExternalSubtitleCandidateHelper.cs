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

            var cleanCandidate = sourceCandidates
                .Where(s => !SubtitleLanguageHelper.IsCaptionSubtitleType(GetSubtitleType(s)))
                .OrderByDescending(ScorePrimarySourceCandidate)
                .ThenBy(s => s.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.FileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (cleanCandidate != null)
            {
                return new ExternalSubtitleSourceSelection(cleanCandidate, sourceLanguage);
            }

            if (ignoreCaptions)
            {
                continue;
            }

            var captionCandidate = sourceCandidates
                .Where(s => SubtitleLanguageHelper.IsCaptionSubtitleType(GetSubtitleType(s)))
                .OrderByDescending(ScorePrimarySourceCandidate)
                .ThenBy(s => s.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.FileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (captionCandidate != null)
            {
                return new ExternalSubtitleSourceSelection(captionCandidate, sourceLanguage);
            }
        }

        return null;
    }

    public static bool ShouldSkipAsPrimarySource(Subtitles subtitle)
    {
        return IsTemporarySource(subtitle) || IsSparseSubtitleFile(subtitle);
    }

    public static bool ShouldSkipAsMainTarget(Subtitles subtitle)
    {
        return IsTemporarySource(subtitle) ||
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

    private static int ScorePrimarySourceCandidate(Subtitles subtitle)
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

        return score;
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
