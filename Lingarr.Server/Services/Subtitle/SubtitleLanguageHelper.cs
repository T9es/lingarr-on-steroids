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
public const string TypeFull = "Full";
    public const string TypeSdh = "SDH";
    public const string TypeClosedCaptions = "CC";
    public const string TypeForced = "Forced";
    public const string TypeSignsSongs = "Signs/Songs";
    public const string TypeCommentary = "Commentary";
    public const string TypeUnknown = "Unknown";
    public const string TypeForcedDialogue = "ForcedDialogue";

    /// <summary>
    /// Minimum number of subtitle entries that a forced track must have to be
    /// reclassified from supplemental forced to forced-dialogue. Tracks with
    /// fewer entries are likely signs/songs-only tracks rather than full dialogue.
    /// </summary>
    public const int ForcedDialogueMinimumEntries = 50;

    private static readonly Regex FileNameLanguageTokenRegex = new(
        @"(?<=^|[.\s_\-\[\]\(\)])([a-z]{2,3}(?:-[a-z]{2,4})?)(?=$|[.\s_\-\[\]\(\)])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TokenSeparatorRegex = new(@"[.\s_\-\[\]\(\)\{\}/\\]+", RegexOptions.Compiled);

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

        var subtitleType = DetermineSubtitleType(subtitle);
        var title = subtitle.Title?.ToLowerInvariant() ?? string.Empty;

        // Titles that usually indicate full dialogue tracks
        if (title.Contains("full"))
        {
            score += 25;
        }

        // Check for dialogue FIRST - if present, it's likely a complete track
        bool hasDialogue = title.Contains("dialog") || title.Contains("dialogue");
        bool hasSignsOrSongs = IsSupplementalSubtitleType(subtitleType);

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
        if (IsCaptionSubtitleType(subtitleType))
        {
            score -= 10;
        }

        // Commentary tracks are almost never suitable for translation
        if (string.Equals(subtitleType, TypeCommentary, StringComparison.OrdinalIgnoreCase))
        {
            score -= 100;
        }

        // Prefer non-forced tracks for full dialogue; forced tracks are often partial or effect-only.
if (subtitle.IsForced || IsSupplementalSubtitleType(subtitleType))
        {
            score -= 50;
        }
        else if (IsForcedDialogueType(subtitleType))
        {
            score -= 15;
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

public static string DetermineSubtitleType(EmbeddedSubtitle subtitle)
    {
        return DetermineSubtitleType(subtitle, entryCount: null);
    }

    /// <summary>
    /// Determines the semantic type of a subtitle track, using content heuristics
    /// to override the forced disposition when the track clearly contains full dialogue.
    /// Anime Bluray remuxes commonly mark ALL subtitle tracks as forced, so a
    /// content-based check prevents misclassifying dialogue tracks as supplemental.
    /// </summary>
    public static string DetermineSubtitleType(EmbeddedSubtitle subtitle, int? entryCount)
    {
        var title = subtitle.Title ?? string.Empty;
        var titleType = DetermineSubtitleTypeFromText(title, defaultType: TypeUnknown);

        if (!subtitle.IsForced)
        {
            return titleType;
        }

        if (string.Equals(titleType, TypeCommentary, StringComparison.OrdinalIgnoreCase))
        {
            return TypeCommentary;
        }

        if (string.Equals(titleType, TypeSdh, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(titleType, TypeClosedCaptions, StringComparison.OrdinalIgnoreCase))
        {
            return titleType;
        }

        if (string.Equals(titleType, TypeSignsSongs, StringComparison.OrdinalIgnoreCase))
        {
            return TypeSignsSongs;
        }

        if (string.Equals(titleType, TypeFull, StringComparison.OrdinalIgnoreCase))
        {
            return TypeForcedDialogue;
        }

        if (entryCount.HasValue && entryCount.Value >= ForcedDialogueMinimumEntries)
        {
            return TypeForcedDialogue;
        }

        return TypeForced;
    }

    public static string DetermineSubtitleTypeFromFilename(string? subtitlePath)
    {
        if (string.IsNullOrWhiteSpace(subtitlePath))
        {
            return TypeUnknown;
        }

        var baseName = Path.GetFileNameWithoutExtension(subtitlePath);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return TypeFull;
        }

        var tokens = TokenSeparatorRegex
            .Split(baseName.ToLowerInvariant())
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToList();
        if (tokens.Count == 0)
        {
            return TypeFull;
        }

        var lastLanguageTokenIndex = -1;
        for (var index = tokens.Count - 1; index >= 0; index--)
        {
            if (TryNormalizeKnownLanguageCode(tokens[index], out _))
            {
                lastLanguageTokenIndex = index;
                break;
            }
        }

        if (lastLanguageTokenIndex >= 0)
        {
            var suffixTokens = tokens
                .Skip(lastLanguageTokenIndex + 1)
                .Where(token => !IsGeneratedSubtitleMarkerToken(token))
                .ToList();

            return DetermineSubtitleTypeFromRoleTokens(suffixTokens, defaultType: TypeFull);
        }

        if (tokens.Count <= 4)
        {
            return DetermineSubtitleTypeFromRoleTokens(tokens, defaultType: TypeFull);
        }

        return TypeFull;
    }

public static bool IsSupplementalSubtitleType(string? subtitleType)
    {
        return string.Equals(subtitleType, TypeForced, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(subtitleType, TypeSignsSongs, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true if the subtitle type represents a forced track that contains
    /// enough entries to be treated as dialogue (rather than signs/songs only).
    /// </summary>
    public static bool IsForcedDialogueType(string? subtitleType)
    {
        return string.Equals(subtitleType, TypeForcedDialogue, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCaptionSubtitleType(string? subtitleType)
    {
        return string.Equals(subtitleType, TypeSdh, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(subtitleType, TypeClosedCaptions, StringComparison.OrdinalIgnoreCase);
    }

public static string? GetSupplementalOutputCaption(string? subtitleType)
    {
        if (string.Equals(subtitleType, TypeForced, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(subtitleType, TypeForcedDialogue, StringComparison.OrdinalIgnoreCase))
        {
            return "forced";
        }

        if (string.Equals(subtitleType, TypeSignsSongs, StringComparison.OrdinalIgnoreCase))
        {
            return "signs";
        }

        return null;
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

    private static string DetermineSubtitleTypeFromText(string? text, string defaultType)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return defaultType;
        }

        var normalized = text.ToLowerInvariant();
        var tokens = TokenSeparatorRegex
            .Split(normalized)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (tokens.Contains("commentary") ||
            normalized.Contains("director commentary", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("audio commentary", StringComparison.OrdinalIgnoreCase))
        {
            return TypeCommentary;
        }

        if (tokens.Contains("forced") ||
            tokens.Contains("force") ||
            tokens.Contains("foreign") ||
            normalized.Contains("foreign only", StringComparison.OrdinalIgnoreCase))
        {
            return TypeForced;
        }

        if (tokens.Contains("sign") ||
            tokens.Contains("signs") ||
            tokens.Contains("song") ||
            tokens.Contains("songs") ||
            tokens.Contains("karaoke") ||
            tokens.Contains("lyrics") ||
            normalized.Contains("signs and songs", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("signs/songs", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("songs & signs", StringComparison.OrdinalIgnoreCase))
        {
            return TypeSignsSongs;
        }

        if (tokens.Contains("sdh") ||
            normalized.Contains("hearing impaired", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("hearing-impaired", StringComparison.OrdinalIgnoreCase) ||
            tokens.Contains("deaf"))
        {
            return TypeSdh;
        }

        if (tokens.Contains("cc") ||
            normalized.Contains("closed caption", StringComparison.OrdinalIgnoreCase))
        {
            return TypeClosedCaptions;
        }

        if (tokens.Contains("full") ||
            tokens.Contains("complete") ||
            tokens.Contains("dialog") ||
            tokens.Contains("dialogue"))
        {
            return TypeFull;
        }

        return defaultType;
    }

    private static string DetermineSubtitleTypeFromRoleTokens(IReadOnlyCollection<string> tokens, string defaultType)
    {
        if (tokens.Count == 0)
        {
            return defaultType;
        }

        if (tokens.Contains("commentary") ||
            ContainsAdjacentTokens(tokens, "director", "commentary") ||
            ContainsAdjacentTokens(tokens, "audio", "commentary"))
        {
            return TypeCommentary;
        }

        if (tokens.Contains("forced") ||
            tokens.Contains("force") ||
            tokens.Contains("foreign") ||
            ContainsAdjacentTokens(tokens, "foreign", "only"))
        {
            return TypeForced;
        }

        if (tokens.Contains("sign") ||
            tokens.Contains("signs") ||
            tokens.Contains("song") ||
            tokens.Contains("songs") ||
            tokens.Contains("karaoke") ||
            tokens.Contains("lyrics") ||
            ContainsAdjacentTokens(tokens, "signs", "songs") ||
            ContainsAdjacentTokens(tokens, "songs", "signs"))
        {
            return TypeSignsSongs;
        }

        if (tokens.Contains("sdh") ||
            tokens.Contains("deaf") ||
            ContainsAdjacentTokens(tokens, "hearing", "impaired"))
        {
            return TypeSdh;
        }

        if (tokens.Contains("cc") ||
            ContainsAdjacentTokens(tokens, "closed", "caption") ||
            ContainsAdjacentTokens(tokens, "closed", "captions"))
        {
            return TypeClosedCaptions;
        }

        if (tokens.Contains("full") ||
            tokens.Contains("complete") ||
            tokens.Contains("dialog") ||
            tokens.Contains("dialogue"))
        {
            return TypeFull;
        }

        return defaultType;
    }

    private static bool ContainsAdjacentTokens(IReadOnlyCollection<string> tokens, string first, string second)
    {
        if (tokens.Count < 2)
        {
            return false;
        }

        var previous = string.Empty;
        foreach (var token in tokens)
        {
            if (string.Equals(previous, first, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(token, second, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            previous = token;
        }

        return false;
    }

    private static bool IsGeneratedSubtitleMarkerToken(string token)
    {
        return string.Equals(token, "ai", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(token, "sztuczna", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(token, "inteligencja", StringComparison.OrdinalIgnoreCase);
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

