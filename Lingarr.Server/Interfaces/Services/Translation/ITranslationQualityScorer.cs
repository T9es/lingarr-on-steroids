namespace Lingarr.Server.Interfaces.Services.Translation;

/// <summary>
/// Scores translation quality between language pairs for auto source selection.
/// Uses NLLB-200 spBLEU scores, LLM quality tiers, and language family heuristics.
/// </summary>
public interface ITranslationQualityScorer
{
    /// <summary>
    /// Scores how well sourceLanguage translates into targetLanguage.
    /// Range: 0-100 where 50+ is considered acceptable for auto fallback.
    /// Both languages should be ISO 639-1 two-letter codes (e.g., "en", "pl").
    /// Returns null if the language pair cannot be scored at all.
    /// </summary>
    double? ScoreDirection(string sourceLanguage, string targetLanguage);

    /// <summary>
    /// Returns true if the score meets the minimum quality threshold for auto fallback.
    /// </summary>
    bool IsAcceptableForAutoFallback(double score);

    /// <summary>
    /// The minimum score required for a language pair to be used as auto fallback.
    /// </summary>
    double MinimumAcceptableScore { get; }
}
