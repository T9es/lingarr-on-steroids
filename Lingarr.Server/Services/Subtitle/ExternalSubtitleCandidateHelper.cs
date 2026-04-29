using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Subtitle;

public static class ExternalSubtitleCandidateHelper
{
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
