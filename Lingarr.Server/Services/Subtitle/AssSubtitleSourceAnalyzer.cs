using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Subtitle;

internal sealed record AssSubtitleSourceAnalysisEntry(
    int Position,
    string ProviderVisibleText,
    bool IsTranslatable,
    int RawSourceCharCount,
    int ProviderVisibleCharCount,
    bool HasDrawingCommands,
    bool HasPositionedTypesetting,
    string? StyleName);

internal sealed record AssSubtitleSourceAnalysis(
    int TotalEvents,
    int TranslatableEvents,
    int SkippedEvents,
    int DrawingEvents,
    int RawSourceChars,
    int ProviderVisibleChars,
    int UniqueProviderTextCount,
    int DuplicateTranslatableEvents,
    double DuplicateRatio,
    double AverageProviderCharsPerTranslatableCue,
    string? DominantStyleName,
    int DominantStyleCount,
    bool HasHighDrawingDensity,
    bool HasHighDuplicateDensity,
    bool HasFragmentedText,
    bool HasSignsDump,
    bool HasExplosiveCueCount,
    bool IsPathological)
{
    public int ContentScoreAdjustment =>
        (HasHighDrawingDensity ? -70 : 0) +
        (HasHighDuplicateDensity ? -60 : 0) +
        (HasFragmentedText ? -50 : 0) +
        (HasSignsDump ? -70 : 0) +
        (HasExplosiveCueCount ? -70 : 0);
}

internal static class AssSubtitleSourceAnalyzer
{
    public static AssSubtitleSourceAnalysis Analyze(IReadOnlyList<AssSubtitleSourceAnalysisEntry> entries)
    {
        if (entries.Count == 0)
        {
            return new AssSubtitleSourceAnalysis(
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                null,
                0,
                false,
                false,
                false,
                false,
                false,
                false);
        }

        var translatableEntries = entries
            .Where(entry => entry.IsTranslatable && !string.IsNullOrWhiteSpace(entry.ProviderVisibleText))
            .ToList();
        var translatableCount = translatableEntries.Count;
        var drawingCount = entries.Count(entry => entry.HasDrawingCommands);
        var providerVisibleChars = entries.Sum(entry => entry.ProviderVisibleCharCount);
        var rawSourceChars = entries.Sum(entry => entry.RawSourceCharCount);
        var uniqueProviderTextCount = translatableEntries
            .Select(entry => ProviderTextDeduper.Normalize(entry.ProviderVisibleText))
            .Distinct(StringComparer.Ordinal)
            .Count();
        var duplicateTranslatableEvents = Math.Max(0, translatableCount - uniqueProviderTextCount);
        var duplicateRatio = translatableCount == 0
            ? 0
            : (double)duplicateTranslatableEvents / translatableCount;
        var averageProviderCharsPerTranslatableCue = translatableCount == 0
            ? 0
            : (double)translatableEntries.Sum(entry => entry.ProviderVisibleCharCount) / translatableCount;
        var dominantStyle = entries
            .Select(entry => entry.StyleName)
            .Where(style => !string.IsNullOrWhiteSpace(style))
            .GroupBy(style => style!, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .FirstOrDefault();
        var dominantStyleName = dominantStyle?.Key;
        var dominantStyleCount = dominantStyle?.Count() ?? 0;

        var hasHighDrawingDensity = drawingCount >= 500 &&
                                    (double)drawingCount / entries.Count >= 0.35;
        var hasHighDuplicateDensity = translatableCount >= 200 &&
                                      duplicateRatio >= 0.6 &&
                                      uniqueProviderTextCount <= translatableCount / 3;
        var hasFragmentedText = translatableCount >= 200 &&
                                averageProviderCharsPerTranslatableCue > 0 &&
                                averageProviderCharsPerTranslatableCue <= 5;
        var positionedTypesettingCount = entries.Count(entry => entry.HasPositionedTypesetting);
        var positionedTypesettingRatio = entries.Count == 0
            ? 0
            : (double)positionedTypesettingCount / entries.Count;
        var hasSignsDump = entries.Count >= 20_000 && positionedTypesettingRatio >= 0.6;
        var hasExplosiveCueCount = entries.Count > 100_000;

        return new AssSubtitleSourceAnalysis(
            entries.Count,
            translatableCount,
            entries.Count - translatableCount,
            drawingCount,
            rawSourceChars,
            providerVisibleChars,
            uniqueProviderTextCount,
            duplicateTranslatableEvents,
            duplicateRatio,
            averageProviderCharsPerTranslatableCue,
            dominantStyleName,
            dominantStyleCount,
            hasHighDrawingDensity,
            hasHighDuplicateDensity,
            hasFragmentedText,
            hasSignsDump,
            hasExplosiveCueCount,
            hasHighDrawingDensity ||
            hasHighDuplicateDensity ||
            hasFragmentedText ||
            hasSignsDump ||
            hasExplosiveCueCount);
    }

    public static async Task<AssSubtitleSourceAnalysis?> AnalyzeExtractedSubtitleAsync(
        EmbeddedSubtitle subtitle,
        ISubtitleService subtitleService,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subtitle.ExtractedPath) || !File.Exists(subtitle.ExtractedPath))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var subtitles = await subtitleService.ReadSubtitles(subtitle.ExtractedPath);
        return AnalyzeSubtitleItems(subtitles);
    }

    public static AssSubtitleSourceAnalysis AnalyzeSubtitleItems(IReadOnlyList<SubtitleItem> subtitles)
    {
        var entries = subtitles
            .Select(subtitleItem =>
            {
                var structure = SubtitleTextStructureFactory.Create(
                    subtitleItem,
                    stripSubtitleFormatting: false,
                    preserveAssFormatting: false);
                var providerText = structure.ProviderVisibleText;
                return CreateEntry(
                    subtitleItem,
                    structure,
                    providerText,
                    !string.IsNullOrWhiteSpace(providerText) && !SubtitleFormatterService.IsMeaningless(providerText.Trim()),
                    string.Join(" ", subtitleItem.Lines).Length);
            })
            .ToList();

        return Analyze(entries);
    }

    public static AssSubtitleSourceAnalysisEntry CreateEntry(
        SubtitleItem subtitle,
        SubtitleTextStructure structure,
        string providerText,
        bool isTranslatable,
        int rawSourceCharCount)
    {
        return new AssSubtitleSourceAnalysisEntry(
            subtitle.Position,
            providerText,
            isTranslatable,
            rawSourceCharCount,
            structure.ProviderVisibleCharCount,
            HasDrawingCommands(subtitle),
            HasPositionedTypesetting(subtitle),
            subtitle.SsaDialogue?.Style);
    }

    private static bool HasPositionedTypesetting(SubtitleItem subtitle)
    {
        return subtitle.Lines.Any(line =>
            line.Contains(@"\pos", StringComparison.OrdinalIgnoreCase) ||
            line.Contains(@"\fscx", StringComparison.OrdinalIgnoreCase) ||
            line.Contains(@"\fscy", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasDrawingCommands(SubtitleItem subtitle)
    {
        return subtitle.Lines.Any(line =>
            line.Contains(@"{\p", StringComparison.OrdinalIgnoreCase) ||
            line.Contains(@"\p1", StringComparison.OrdinalIgnoreCase) ||
            line.Contains(@"\p2", StringComparison.OrdinalIgnoreCase) ||
            line.Contains(@"\p3", StringComparison.OrdinalIgnoreCase) ||
            line.Contains(@"\p4", StringComparison.OrdinalIgnoreCase) ||
            line.Contains(@"\p5", StringComparison.OrdinalIgnoreCase) ||
            line.Contains(@"\p6", StringComparison.OrdinalIgnoreCase) ||
            line.Contains(@"\p7", StringComparison.OrdinalIgnoreCase) ||
            line.Contains(@"\p8", StringComparison.OrdinalIgnoreCase) ||
            line.Contains(@"\p9", StringComparison.OrdinalIgnoreCase));
    }
}
