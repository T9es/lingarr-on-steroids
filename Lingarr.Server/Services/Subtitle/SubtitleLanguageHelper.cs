using Lingarr.Core.Entities;

namespace Lingarr.Server.Services.Subtitle;

/// <summary>
/// Helpers for working with subtitle language codes and scoring embedded subtitle streams.
/// Centralizes language normalization so we handle common 2-letter / 3-letter and
/// region-specific variants consistently (e.g. "en" / "eng" / "en-US", "ja" / "jpn").
/// </summary>
public static class SubtitleLanguageHelper
{
    private static readonly Dictionary<string, string> Iso639Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // English
        ["en"] = "en",
        ["eng"] = "en",
        ["en-us"] = "en",
        ["en-gb"] = "en",

        // Japanese
        ["ja"] = "ja",
        ["jpn"] = "ja",

        // German
        ["de"] = "de",
        ["deu"] = "de",
        ["ger"] = "de",

        // French
        ["fr"] = "fr",
        ["fra"] = "fr",
        ["fre"] = "fr",

        // Spanish
        ["es"] = "es",
        ["spa"] = "es",

        // Portuguese
        ["pt"] = "pt",
        ["por"] = "pt",
        ["pt-br"] = "pt",

        // Italian
        ["it"] = "it",
        ["ita"] = "it",

        // Dutch
        ["nl"] = "nl",
        ["nld"] = "nl",
        ["dut"] = "nl",

        // Romanian
        ["ro"] = "ro",
        ["ron"] = "ro",
        ["rum"] = "ro",

        // Polish
        ["pl"] = "pl",
        ["pol"] = "pl",

        // Russian
        ["ru"] = "ru",
        ["rus"] = "ru",

        // Korean
        ["ko"] = "ko",
        ["kor"] = "ko",

        // Hindi
        ["hi"] = "hi",
        ["hin"] = "hi",

        // Chinese (generic)
        ["zh"] = "zh",
        ["zho"] = "zh",
        ["chi"] = "zh",

        // Czech
        ["cs"] = "cs",
        ["ces"] = "cs",
        ["cze"] = "cs",

        // Turkish
        ["tr"] = "tr",
        ["tur"] = "tr"
    };

    /// <summary>
    /// Normalizes a language code to a comparable form, collapsing common
    /// 3-letter ISO codes and regional variants to their 2-letter base code.
    /// </summary>
    public static string NormalizeLanguageCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        var normalized = code.Trim().ToLowerInvariant();

        // Try exact map first (handles 2-letter, 3-letter and known regional variants)
        if (Iso639Map.TryGetValue(normalized, out var mapped))
        {
            return mapped;
        }

        // Handle region codes like "en-us" or "pt-br" by looking at the base code
        var dashIndex = normalized.IndexOf('-');
        if (dashIndex > 0)
        {
            var baseCode = normalized[..dashIndex];
            if (Iso639Map.TryGetValue(baseCode, out var baseMapped))
            {
                return baseMapped;
            }

            return baseCode;
        }

        // As a last resort for unknown 3-letter codes, fall back to the first 2 letters
        if (normalized.Length == 3)
        {
            var twoLetter = normalized[..2];
            if (Iso639Map.TryGetValue(twoLetter, out var twoLetterMapped))
            {
                return twoLetterMapped;
            }

            return twoLetter;
        }

        return normalized;
    }

    /// <summary>
    /// Determines whether an embedded subtitle language matches a configured source language.
    /// Uses NormalizeLanguageCode for tolerant comparison.
    /// </summary>
    public static bool LanguageMatches(string? subtitleLanguage, string? sourceLanguage)
    {
        if (string.IsNullOrWhiteSpace(subtitleLanguage) || string.IsNullOrWhiteSpace(sourceLanguage))
        {
            return false;
        }

        var sub = NormalizeLanguageCode(subtitleLanguage);
        var src = NormalizeLanguageCode(sourceLanguage);

        if (string.IsNullOrEmpty(sub) || string.IsNullOrEmpty(src))
        {
            return false;
        }

        return string.Equals(sub, src, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Scores an embedded subtitle candidate based on language match, title heuristics and flags.
    /// Higher scores indicate better candidates for full dialogue translation.
    /// </summary>
    public static int ScoreSubtitleCandidate(EmbeddedSubtitle subtitle, string? preferredLanguage)
    {
        var score = 0;

        if (LanguageMatches(subtitle.Language, preferredLanguage))
        {
            score += 50;
        }

        var title = subtitle.Title?.ToLowerInvariant() ?? string.Empty;

        // Titles that usually indicate full dialogue tracks
        if (title.Contains("full"))
        {
            score += 25;
        }

        if (title.Contains("dialog") || title.Contains("dialogue"))
        {
            score += 20;
        }

        if (title.Contains("sub") || title.Contains("subtitle"))
        {
            score += 10;
        }

        // Titles that typically indicate signs/songs/karaoke-only tracks
        if (title.Contains("sign") || title.Contains("song") || title.Contains("karaoke"))
        {
            score -= 40;
        }

        // Prefer non-forced tracks for full dialogue; forced tracks are often partial or effect-only.
        if (subtitle.IsForced)
        {
            score -= 10;
        }
        else
        {
            score += 5;
        }

        // Being the default stream is a weak positive signal (unless heavily penalized by title heuristics).
        if (subtitle.IsDefault)
        {
            score += 5;
        }

        return score;
    }
    /// <summary>
    /// Minimum quality threshold for a subtitle track to be considered "acceptable".
    /// Tracks below this threshold will not receive language priority bonuses.
    /// </summary>
    private const int QualityThreshold = 40;
    
    /// <summary>
    /// Priority bonus per language rank position (earlier languages get higher bonuses).
    /// </summary>
    private const int LanguagePriorityBonus = 80;
    
    /// <summary>
    /// Finds the best matching embedded subtitle from a list of candidates based on configured language priorities.
    /// Uses a quality threshold approach: higher-priority languages receive bonuses only if they meet minimum quality.
    /// This prevents selecting partial/garbage tracks (e.g., "Signs & Songs") over complete dialogue tracks in other languages.
    /// </summary>
    public static (EmbeddedSubtitle? Subtitle, string MatchedLanguage) FindBestMatch(
        List<EmbeddedSubtitle> candidates, 
        List<string> configuredLanguages)
    {
        if (candidates == null || !candidates.Any() || configuredLanguages == null || !configuredLanguages.Any())
        {
            return (null, string.Empty);
        }

        EmbeddedSubtitle? bestSubtitle = null;
        string bestLanguage = string.Empty;
        int bestScore = int.MinValue;

        // Score all candidates across all configured languages
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Language))
            {
                continue;
            }

            // Find if this candidate matches any configured language
            for (var i = 0; i < configuredLanguages.Count; i++)
            {
                var configuredLanguage = configuredLanguages[i];
                if (LanguageMatches(candidate.Language, configuredLanguage))
                {
                    var baseScore = ScoreSubtitleCandidate(candidate, configuredLanguage);
                    var totalScore = baseScore;
                    
                    // Apply language priority bonus ONLY if track meets quality threshold
                    // This ensures garbage high-priority tracks don't beat good low-priority ones
                    if (baseScore >= QualityThreshold)
                    {
                        var priorityBonus = (configuredLanguages.Count - i) * LanguagePriorityBonus;
                        totalScore += priorityBonus;
                    }
                    
                    if (totalScore > bestScore)
                    {
                        bestScore = totalScore;
                        bestSubtitle = candidate;
                        bestLanguage = configuredLanguage;
                    }
                    break; // Matched this language, no need to check others for this candidate
                }
            }
        }

        return (bestSubtitle, bestLanguage);
    }
}

