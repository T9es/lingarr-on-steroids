using Lingarr.Server.Models.Batch;

namespace Lingarr.Server.Services.Translation;

internal sealed record ProviderTranslationValidationResult(
    IReadOnlySet<int> InvalidPositions,
    IReadOnlySet<int> EchoedPositions,
    IReadOnlySet<int> MismatchedPositions);

internal static class ProviderTranslationValidation
{
    public static ProviderTranslationValidationResult Analyze(
        IReadOnlyList<BatchSubtitleItem> sourceItems,
        IReadOnlyDictionary<int, string> translatedByPosition,
        string? sourceLanguage,
        string? targetLanguage)
    {
        var echoedPositions = TranslationEchoGuard
            .AnalyzeBatch(sourceItems, translatedByPosition, sourceLanguage, targetLanguage)
            .EchoedPositions
            .ToHashSet();
        var mismatchedPositions = TranslationLanguageGuard
            .AnalyzeBatch(sourceItems, translatedByPosition, targetLanguage)
            .MismatchedPositions
            .ToHashSet();
        var invalidPositions = echoedPositions.ToHashSet();
        invalidPositions.UnionWith(mismatchedPositions);

        return new ProviderTranslationValidationResult(
            invalidPositions,
            echoedPositions,
            mismatchedPositions);
    }

    public static HashSet<int> FindInvalidPositions(
        IReadOnlyList<BatchSubtitleItem> sourceItems,
        IReadOnlyDictionary<int, string> translatedByPosition,
        string? sourceLanguage,
        string? targetLanguage)
    {
        return Analyze(sourceItems, translatedByPosition, sourceLanguage, targetLanguage)
            .InvalidPositions
            .ToHashSet();
    }
}
