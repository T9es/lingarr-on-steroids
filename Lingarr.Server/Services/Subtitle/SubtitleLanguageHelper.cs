using System.Globalization;
using System.Text.RegularExpressions;
using Lingarr.Core.Entities;

namespace Lingarr.Server.Services.Subtitle;

/// <summary>
/// Helpers for working with subtitle language codes and scoring embedded subtitle streams.
/// Centralizes language normalization so we handle common 2-letter / 3-letter and
/// region-specific variants consistently (e.g. "en" / "eng" / "en-US", "ja" / "jpn").
/// </summary>
public static class SubtitleLanguageHelper
{
    private static readonly Regex FileNameLanguageTokenRegex = new(
        @"(?<=^|[.\s_\-\[\]\(\)])([a-z]{2,3}(?:-[a-z]{2,4})?)(?=$|[.\s_\-\[\]\(\)])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

    private static readonly Dictionary<string, string> CultureLanguageMap = BuildCultureLanguageMap();

    /// <summary>
    /// Normalizes a language code to a comparable form, collapsing common
    /// 3-letter ISO codes and regional variants to their 2-letter base code.
    /// </summary>
    public static string NormalizeLanguageCode(string? code)
    {
        if (TryNormalizeKnownLanguageCode(code, out var knownCode))
        {
            return knownCode;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        var normalized = NormalizeLanguageToken(code);

        // Handle region codes like "en-us" or "pt-br" by looking at the base code
        var dashIndex = normalized.IndexOf('-');
        if (dashIndex > 0)
        {
            var baseCode = normalized[..dashIndex];
            if (TryNormalizeKnownLanguageCode(baseCode, out var baseMapped))
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

    public static bool TryNormalizeKnownLanguageCode(string? value, out string code)
    {
        code = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = NormalizeLanguageToken(value);
        if (TryNormalizeKnownLanguageCodeCore(normalized, out code))
        {
            return true;
        }

        var dashIndex = normalized.IndexOf('-');
        if (dashIndex > 0)
        {
            var baseCode = normalized[..dashIndex];
            if (TryNormalizeKnownLanguageCodeCore(baseCode, out code))
            {
                return true;
            }
        }

        return false;
    }

    public static string? DetectLanguageFromFileName(
        string fileName,
        IReadOnlyCollection<string>? configuredLanguages = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var normalizedConfiguredLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (configuredLanguages != null)
        {
            foreach (var configuredLanguage in configuredLanguages)
            {
                if (TryNormalizeKnownLanguageCode(configuredLanguage, out var normalizedConfiguredLanguage))
                {
                    normalizedConfiguredLanguages.Add(normalizedConfiguredLanguage);
                }
            }
        }

        var useConfiguredFilter = normalizedConfiguredLanguages.Count > 0;
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return null;
        }

        var matches = FileNameLanguageTokenRegex.Matches(baseName);
        for (var index = matches.Count - 1; index >= 0; index--)
        {
            var token = matches[index].Groups[1].Value;
            if (!TryNormalizeKnownLanguageCode(token, out var normalizedCode))
            {
                continue;
            }

            if (useConfiguredFilter && !normalizedConfiguredLanguages.Contains(normalizedCode))
            {
                continue;
            }

            return normalizedCode;
        }

        return null;
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
    public static int ScoreSubtitleCandidate(
        EmbeddedSubtitle subtitle,
        string? preferredLanguage,
        int contentScoreAdjustment = 0)
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

        // Check for dialogue FIRST - if present, it's likely a complete track
        bool hasDialogue = title.Contains("dialog") || title.Contains("dialogue");
        bool hasSignsOrSongs = title.Contains("sign") || title.Contains("song") || title.Contains("karaoke");

        if (hasDialogue)
        {
            score += 30; // Increased from 20, represents a complete track
        }
        else if (hasSignsOrSongs)
        {
            score -= 40; // Only penalize if no dialogue indicator is present
        }

        if (title.Contains("sub") || title.Contains("subtitle"))
        {
            score += 10;
        }

        // Penalize SDH/Hearing Impaired/CC tracks as they often contain "poison" content (sound effects, lyrics)
        if (title.Contains("sdh") || title.Contains("hearing impaired") || title.Contains("cc") || title.Contains("closed caption"))
        {
            score -= 10;
        }

        // Commentary tracks are almost never suitable for translation
        if (title.Contains("commentary"))
        {
            score -= 20;
        }

        // Prefer non-forced tracks for full dialogue; forced tracks are often partial or effect-only.
        if (subtitle.IsForced)
        {
            score -= 50;
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

        score += contentScoreAdjustment;
        return score;
    }
    /// <summary>
    /// Minimum quality threshold for a subtitle track to be considered "acceptable".
    /// Tracks below this threshold will not receive language priority bonuses.
    /// </summary>
    private const int QualityThreshold = 30;
    
    /// <summary>
    /// Priority bonus per language rank position (earlier languages get higher bonuses).
    /// </summary>
    private const int LanguagePriorityBonus = 80;
    
    /// <summary>
    /// Finds the best matching embedded subtitle from a list of candidates based on configured language priorities.
    /// Uses a quality threshold approach: higher-priority languages receive bonuses only if they meet minimum quality.
    /// This prevents selecting partial/garbage tracks (e.g., "Signs &amp; Songs") over complete dialogue tracks in other languages.
    /// </summary>
    public static (EmbeddedSubtitle? Subtitle, string MatchedLanguage) FindBestMatch(
        List<EmbeddedSubtitle> candidates, 
        List<string> configuredLanguages,
        Func<EmbeddedSubtitle, int>? contentScoreAdjustmentSelector = null)
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
                    var contentScoreAdjustment = contentScoreAdjustmentSelector?.Invoke(candidate) ?? 0;
                    var baseScore = ScoreSubtitleCandidate(candidate, configuredLanguage, contentScoreAdjustment);
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

    private static bool TryNormalizeKnownLanguageCodeCore(string normalized, out string code)
    {
        code = string.Empty;

        if (Iso639Map.TryGetValue(normalized, out var isoMappedCode))
        {
            code = isoMappedCode;
            return true;
        }

        if (CultureLanguageMap.TryGetValue(normalized, out var cultureMappedCode))
        {
            code = cultureMappedCode;
            return true;
        }

        return false;
    }

    private static string NormalizeLanguageToken(string value)
    {
        return value
            .Trim()
            .Trim('[', ']', '(', ')', '{', '}')
            .Replace('_', '-')
            .ToLowerInvariant();
    }

    private static Dictionary<string, string> BuildCultureLanguageMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var culture in CultureInfo.GetCultures(CultureTypes.AllCultures))
        {
            var twoLetterCode = culture.TwoLetterISOLanguageName?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(twoLetterCode) ||
                twoLetterCode == "iv" ||
                twoLetterCode.Length != 2)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(culture.Name))
            {
                map[NormalizeLanguageToken(culture.Name)] = twoLetterCode;
            }

            if (!string.IsNullOrWhiteSpace(culture.ThreeLetterISOLanguageName))
            {
                map[NormalizeLanguageToken(culture.ThreeLetterISOLanguageName)] = twoLetterCode;
            }

            map[NormalizeLanguageToken(twoLetterCode)] = twoLetterCode;
        }

        return map;
    }
}

