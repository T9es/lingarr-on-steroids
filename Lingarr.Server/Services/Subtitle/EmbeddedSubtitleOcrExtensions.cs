using Lingarr.Core.Entities;
using Lingarr.Core.Enum;

namespace Lingarr.Server.Services.Subtitle;

public static class EmbeddedSubtitleOcrExtensions
{
    public static bool HasUsableOcr(this EmbeddedSubtitle subtitle)
    {
        return !subtitle.IsTextBased &&
               subtitle.OcrStatus is SubtitleOcrStatus.Succeeded or SubtitleOcrStatus.Approved &&
               !string.IsNullOrWhiteSpace(subtitle.OcrExtractedPath) &&
               File.Exists(subtitle.OcrExtractedPath);
    }

    public static bool IsReadableSource(this EmbeddedSubtitle subtitle)
    {
        return subtitle.IsTextBased || subtitle.HasUsableOcr();
    }

    public static string? GetReadableSourcePath(this EmbeddedSubtitle subtitle)
    {
        return subtitle.HasUsableOcr()
            ? subtitle.OcrExtractedPath
            : subtitle.ExtractedPath;
    }

    public static string GetReadableSourceFormat(this EmbeddedSubtitle subtitle)
    {
        return subtitle.HasUsableOcr()
            ? ".srt"
            : SubtitleOutputModeHelper.NormalizeFormat(subtitle.CodecName);
    }
}
