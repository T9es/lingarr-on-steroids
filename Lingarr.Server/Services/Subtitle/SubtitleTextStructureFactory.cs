using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Subtitle;

internal static class SubtitleTextStructureFactory
{
    public static SubtitleTextStructure Create(
        SubtitleItem subtitle,
        bool stripSubtitleFormatting,
        bool preserveAssFormatting)
    {
        var sourceLines = GetSourceLines(subtitle, stripSubtitleFormatting, preserveAssFormatting);
        if (sourceLines.Count == 0)
        {
            return new SubtitleTextStructure(SubtitleStructureMode.PlainText, [string.Empty], [
                new SubtitleTextLine(
                    0,
                    0,
                    [new SubtitleTextPart(SubtitleTextPartKind.Text, string.Empty, true, string.Empty)],
                    string.Empty)
            ]);
        }

        if (stripSubtitleFormatting && !preserveAssFormatting)
        {
            var plainLines = sourceLines
                .Select((line, index) => new SubtitleTextLine(
                    index,
                    0,
                    [new SubtitleTextPart(SubtitleTextPartKind.Text, line, true, line)],
                    string.Empty))
                .ToList();
            return new SubtitleTextStructure(SubtitleStructureMode.PlainText, sourceLines, plainLines);
        }

        if (IsAssSubtitle(subtitle) || AssTextStructureParser.ContainsAssSyntax(sourceLines))
        {
            var assLines = new AssTextStructureParser().Parse(sourceLines);
            return new SubtitleTextStructure(SubtitleStructureMode.Ass, sourceLines, assLines);
        }

        var inlineLines = new InlineMarkupStructureParser().Parse(sourceLines);
        return new SubtitleTextStructure(SubtitleStructureMode.InlineMarkup, sourceLines, inlineLines);
    }

    public static List<string> GetSourceLines(
        SubtitleItem subtitle,
        bool stripSubtitleFormatting,
        bool preserveAssFormatting)
    {
        var usePlaintextInput = stripSubtitleFormatting && !preserveAssFormatting;
        return usePlaintextInput
            ? subtitle.PlaintextLines
            : subtitle.Lines;
    }

    public static bool IsAssSubtitle(SubtitleItem subtitle)
    {
        return subtitle.SsaDialogue != null || subtitle.SsaFormat != null;
    }
}
