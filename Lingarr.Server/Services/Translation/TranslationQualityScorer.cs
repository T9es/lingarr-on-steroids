using System.Globalization;
using Lingarr.Server.Interfaces.Services.Translation;

namespace Lingarr.Server.Services.Translation;

/// <summary>
/// Scores translation quality between language pairs using three data layers:
/// 1. NLLB-200 FLORES-200 spBLEU scores (50% weight)
/// 2. LLM quality tiers per source language (30% weight)
/// 3. Language family proximity heuristic (20% weight)
/// </summary>
public class TranslationQualityScorer : ITranslationQualityScorer
{
    private const double MinimumAutoScore = 50.0;
    private const double NllbWeight = 0.50;
    private const double LlmTierWeight = 0.30;
    private const double FamilyWeight = 0.20;

    // Maximum spBLEU observed in NLLB-200 FLORES-200 devtest for 3B model
    private const double MaxSpBleu = 56.0;

    // FLORES-200 uses ISO 639-3 codes with script suffix.
    // This maps ISO 639-1 two-letter codes to their FLORES-200 representation.
    private static readonly Dictionary<string, string> Iso6391ToFlores = new()
    {
        // Germanic
        { "en", "eng_Latn" }, { "de", "deu_Latn" }, { "nl", "nld_Latn" },
        { "da", "dan_Latn" }, { "sv", "swe_Latn" }, { "no", "nob_Latn" },
        { "nb", "nob_Latn" }, { "nn", "nno_Latn" }, { "is", "isl_Latn" },
        { "fy", "fry_Latn" }, { "af", "afr_Latn" },
        // Romance
        { "fr", "fra_Latn" }, { "es", "spa_Latn" }, { "it", "ita_Latn" },
        { "pt", "por_Latn" }, { "ro", "ron_Latn" }, { "ca", "cat_Latn" },
        { "gl", "glg_Latn" },
        // Slavic
        { "pl", "pol_Latn" }, { "cs", "ces_Latn" }, { "sk", "slk_Latn" },
        { "ru", "rus_Cyrl" }, { "uk", "ukr_Cyrl" }, { "bg", "bul_Cyrl" },
        { "sr", "srp_Cyrl" }, { "hr", "hrv_Latn" }, { "sl", "slv_Latn" },
        { "be", "bel_Cyrl" }, { "mk", "mkd_Cyrl" }, { "bs", "bos_Latn" },
        // Uralic
        { "hu", "hun_Latn" }, { "fi", "fin_Latn" }, { "et", "est_Latn" },
        // Baltic
        { "lt", "lit_Latn" }, { "lv", "lvs_Latn" },
        // Hellenic
        { "el", "ell_Grek" },
        // Celtic
        { "cy", "cym_Latn" }, { "ga", "gle_Latn" }, { "gd", "gla_Latn" },
        // Albanian
        { "sq", "als_Latn" },
        // Armenian
        { "hy", "hye_Armn" },
        // Georgian
        { "ka", "kat_Geor" },
        // Iranian
        { "fa", "pes_Arab" }, { "ps", "pbt_Arab" }, { "ku", "kmr_Latn" },
        // Indo-Aryan
        { "hi", "hin_Deva" }, { "ur", "urd_Arab" }, { "bn", "ben_Beng" },
        { "gu", "guj_Gujr" }, { "mr", "mar_Deva" }, { "pa", "pan_Guru" },
        { "ta", "tam_Taml" }, { "te", "tel_Telu" }, { "kn", "kan_Knda" },
        { "ml", "mal_Mlym" }, { "si", "sin_Sinh" }, { "ne", "npi_Deva" },
        // Turkic
        { "tr", "tur_Latn" }, { "az", "azj_Latn" }, { "kk", "kaz_Cyrl" },
        { "ky", "kir_Cyrl" }, { "uz", "uzn_Latn" }, { "tk", "tuk_Latn" },
        // Semitic
        { "ar", "arb_Arab" }, { "he", "heb_Hebr" }, { "mt", "mlt_Latn" },
        { "am", "amh_Ethi" },
        // East Asian
        { "ja", "jpn_Jpan" }, { "zh", "zho_Hans" }, { "ko", "kor_Hang" },
        { "th", "tha_Thai" }, { "vi", "vie_Latn" }, { "km", "khm_Khmr" },
        { "my", "mya_Mymr" }, { "lo", "lao_Laoo" },
        // Austronesian
        { "id", "ind_Latn" }, { "ms", "zsm_Latn" }, { "tl", "tgl_Latn" },
        { "jw", "jav_Latn" }, { "su", "sun_Latn" },
        // African
        { "sw", "swh_Latn" }, { "zu", "zul_Latn" }, { "xh", "xho_Latn" },
        { "ha", "hau_Latn" }, { "yo", "yor_Latn" }, { "ig", "ibo_Latn" },
        { "so", "som_Latn" }, { "st", "sot_Latn" }, { "tn", "tsn_Latn" },
        { "rw", "kin_Latn" }, { "sn", "sna_Latn" }, { "mg", "plt_Latn" },
        // Other
        { "mn", "khk_Cyrl" }, { "tg", "tgk_Cyrl" },
    };

    // LLM quality tiers per source language (0.55-0.95).
    // Based on WMT25 evaluations and Intento research.
    // Higher = LLMs produce better translations from this language.
    private static readonly Dictionary<string, double> LlmQualityTiers = new()
    {
        // Tier 1: Excellent (0.95) — English, high-resource European
        { "en", 0.95 }, { "de", 0.92 }, { "fr", 0.92 }, { "es", 0.90 },
        // Tier 2: Very Good (0.88) — Major European languages
        { "it", 0.88 }, { "pt", 0.88 }, { "nl", 0.88 }, { "pl", 0.87 },
        { "ru", 0.86 }, { "sv", 0.86 }, { "da", 0.86 }, { "no", 0.86 },
        // Tier 3: Good (0.82) — Well-resourced European
        { "cs", 0.82 }, { "sk", 0.82 }, { "hu", 0.82 }, { "ro", 0.82 },
        { "bg", 0.82 }, { "hr", 0.82 }, { "sr", 0.82 }, { "sl", 0.82 },
        { "uk", 0.82 }, { "el", 0.82 }, { "fi", 0.82 }, { "et", 0.82 },
        { "lt", 0.82 }, { "lv", 0.82 }, { "ca", 0.82 },
        // Tier 4: Moderate (0.75) — Well-resourced Asian
        { "ja", 0.78 }, { "zh", 0.78 }, { "ko", 0.75 }, { "th", 0.75 },
        { "vi", 0.78 }, { "id", 0.75 }, { "ms", 0.75 }, { "tl", 0.75 },
        { "ar", 0.78 }, { "he", 0.78 }, { "tr", 0.78 }, { "fa", 0.75 },
        // Tier 5: Fair (0.68) — Mid-resource languages
        { "hi", 0.72 }, { "bn", 0.70 }, { "ta", 0.70 }, { "te", 0.68 },
        { "kn", 0.68 }, { "ml", 0.68 }, { "mr", 0.68 }, { "gu", 0.68 },
        { "pa", 0.68 }, { "ur", 0.70 }, { "ne", 0.68 }, { "si", 0.65 },
        { "km", 0.65 }, { "my", 0.65 }, { "lo", 0.65 }, { "mn", 0.65 },
        { "kk", 0.65 }, { "az", 0.65 }, { "uz", 0.65 },
        // Tier 6: Low (0.58) — Lower-resource / African languages
        { "sw", 0.62 }, { "zu", 0.58 }, { "xh", 0.58 }, { "ha", 0.58 },
        { "yo", 0.55 }, { "ig", 0.55 }, { "so", 0.55 }, { "st", 0.55 },
        { "tn", 0.55 }, { "sn", 0.55 }, { "mg", 0.55 },
    };

    // Language family grouping for proximity heuristic.
    private static readonly Dictionary<string, string> LanguageFamily = new()
    {
        // Germanic
        { "en", "germanic" }, { "de", "germanic" }, { "nl", "germanic" },
        { "da", "germanic" }, { "sv", "germanic" }, { "no", "germanic" },
        { "nb", "germanic" }, { "nn", "germanic" }, { "is", "germanic" },
        { "fy", "germanic" }, { "af", "germanic" },
        // Romance
        { "fr", "romance" }, { "es", "romance" }, { "it", "romance" },
        { "pt", "romance" }, { "ro", "romance" }, { "ca", "romance" },
        { "gl", "romance" },
        // Slavic
        { "pl", "slavic" }, { "cs", "slavic" }, { "sk", "slavic" },
        { "ru", "slavic" }, { "uk", "slavic" }, { "bg", "slavic" },
        { "sr", "slavic" }, { "hr", "slavic" }, { "sl", "slavic" },
        { "be", "slavic" }, { "mk", "slavic" }, { "bs", "slavic" },
        // Uralic
        { "hu", "uralic" }, { "fi", "uralic" }, { "et", "uralic" },
        // Baltic
        { "lt", "baltic" }, { "lv", "baltic" },
        // Hellenic
        { "el", "hellenic" },
        // Celtic
        { "cy", "celtic" }, { "ga", "celtic" }, { "gd", "celtic" },
        // Albanian
        { "sq", "albanian" },
        // Armenian
        { "hy", "armenian" },
        // Georgian
        { "ka", "georgian" },
        // Iranian
        { "fa", "iranian" }, { "ps", "iranian" }, { "ku", "iranian" },
        // Indo-Aryan
        { "hi", "indoaryan" }, { "ur", "indoaryan" }, { "bn", "indoaryan" },
        { "gu", "indoaryan" }, { "mr", "indoaryan" }, { "pa", "indoaryan" },
        { "ta", "indoaryan" }, { "te", "indoaryan" }, { "kn", "indoaryan" },
        { "ml", "indoaryan" }, { "si", "indoaryan" }, { "ne", "indoaryan" },
        // Turkic
        { "tr", "turkic" }, { "az", "turkic" }, { "kk", "turkic" },
        { "ky", "turkic" }, { "uz", "turkic" }, { "tk", "turkic" },
        // Semitic
        { "ar", "semitic" }, { "he", "semitic" }, { "mt", "semitic" },
        { "am", "semitic" },
        // East Asian
        { "ja", "eastasian" }, { "zh", "eastasian" }, { "ko", "eastasian" },
        // Austroasiatic
        { "th", "taikadai" }, { "vi", "austroasiatic" }, { "km", "austroasiatic" },
        { "my", "sinotibetan" }, { "lo", "taikadai" },
        // Austronesian
        { "id", "austronesian" }, { "ms", "austronesian" }, { "tl", "austronesian" },
        { "jw", "austronesian" }, { "su", "austronesian" },
        // African families
        { "sw", "bantu" }, { "zu", "bantu" }, { "xh", "bantu" },
        { "st", "bantu" }, { "tn", "bantu" }, { "sn", "bantu" },
        { "rw", "bantu" },
        { "ha", "afroasiatic" }, { "so", "afroasiatic" }, { "am", "afroasiatic" },
        { "yo", "nigercongo" }, { "ig", "nigercongo" },
        // Other
        { "mn", "mongolic" }, { "tg", "iranian" },
    };

    // Sub-family bonus for closely related languages within the same family.
    // Higher bonus = more closely related.
    private static readonly Dictionary<string, Dictionary<string, double>> SubFamilyBonus = new()
    {
        ["slavic"] = new()
        {
            ["west"] = 0.15, // Polish, Czech, Slovak
            ["east"] = 0.10, // Russian, Ukrainian, Belarusian
            ["south"] = 0.10, // Bulgarian, Serbian, Croatian, Slovenian, Macedonian
        },
        ["germanic"] = new()
        {
            ["west"] = 0.10, // English, German, Dutch
            ["north"] = 0.15, // Danish, Swedish, Norwegian, Icelandic
        },
        ["romance"] = new()
        {
            ["east"] = 0.10, // Romanian
            ["iberian"] = 0.10, // Spanish, Portuguese, Catalan, Galician
            ["gallian"] = 0.10, // French
        },
    };

    private static readonly Dictionary<string, string> LanguageSubFamily = new()
    {
        { "pl", "west" }, { "cs", "west" }, { "sk", "west" },
        { "ru", "east" }, { "uk", "east" }, { "be", "east" },
        { "bg", "south" }, { "sr", "south" }, { "hr", "south" },
        { "sl", "south" }, { "mk", "south" }, { "bs", "south" },
        { "da", "north" }, { "sv", "north" }, { "no", "north" },
        { "nb", "north" }, { "nn", "north" }, { "is", "north" },
        { "en", "west" }, { "de", "west" }, { "nl", "west" },
        { "fy", "west" }, { "af", "west" },
        { "ro", "east" },
        { "es", "iberian" }, { "pt", "iberian" }, { "ca", "iberian" }, { "gl", "iberian" },
        { "fr", "gallian" },
    };

    private Dictionary<string, double> _nllbScores;
    private readonly string _scoresFilePath;
    private readonly object _loadLock = new();
    private bool _loaded;

    /// <inheritdoc />
    public double MinimumAcceptableScore => MinimumAutoScore;

    public TranslationQualityScorer()
    {
        _scoresFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Statics", "LanguageTranslation", "nllb_scores.tsv");
        _nllbScores = new Dictionary<string, double>();
    }

    /// <summary>
    /// Constructor with custom path for testing.
    /// </summary>
    public TranslationQualityScorer(string scoresFilePath)
    {
        _scoresFilePath = scoresFilePath;
        _nllbScores = new Dictionary<string, double>();
    }

    /// <inheritdoc />
    public double? ScoreDirection(string sourceLanguage, string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(sourceLanguage) ||
            string.IsNullOrWhiteSpace(targetLanguage))
        {
            return null;
        }

        var source = NormalizeLanguageCode(sourceLanguage);
        var target = NormalizeLanguageCode(targetLanguage);

        if (source == null || target == null)
        {
            return null;
        }

        EnsureScoresLoaded();

        var (sourceCode, _) = source.Value;
        var (targetCode, _) = target.Value;
        var nllbScore = LookupNllbScore(sourceCode, targetCode);
        var llmTier = GetLlmTier(sourceCode);
        var familyProximity = GetFamilyProximity(sourceCode, targetCode);

        // If NLLB score is unavailable, fall back to LLM + family only
        if (nllbScore == null)
        {
            return (llmTier * 100 * (LlmTierWeight + FamilyWeight)) +
                   (familyProximity * 100 * FamilyWeight);
        }

        return (nllbScore * NllbWeight) +
               (llmTier * 100 * LlmTierWeight) +
               (familyProximity * 100 * FamilyWeight);
    }

    /// <inheritdoc />
    public bool IsAcceptableForAutoFallback(double score)
    {
        return score >= MinimumAutoScore;
    }

    private (string code, string flores)? NormalizeLanguageCode(string languageCode)
    {
        var normalized = languageCode.Trim().ToLowerInvariant();

        // Check direct ISO 639-1 match first
        if (Iso6391ToFlores.ContainsKey(normalized))
        {
            return (normalized, Iso6391ToFlores[normalized]);
        }

        // Try to resolve via CultureInfo
        try
        {
            var cultures = CultureInfo.GetCultures(CultureTypes.AllCultures);
            foreach (var culture in cultures)
            {
                var twoLetter = culture.TwoLetterISOLanguageName?.ToLowerInvariant();
                if (twoLetter == normalized && !string.IsNullOrWhiteSpace(culture.ThreeLetterISOLanguageName))
                {
                    var threeLetter = culture.ThreeLetterISOLanguageName.ToLowerInvariant();
                    var floresCode = TryBuildFloresCode(threeLetter);
                    if (floresCode != null)
                    {
                        return (twoLetter, floresCode);
                    }
                }
            }
        }
        catch
        {
            // Ignore CultureInfo failures
        }

        return null;
    }

    private string? TryBuildFloresCode(string threeLetterCode)
    {
        // Map ISO 639-2/B to ISO 639-3 for known differences
        var iso6393 = threeLetterCode switch
        {
            "chi" => "zho", // Chinese
            "cze" => "ces", // Czech
            "ger" => "deu", // German
            "dut" => "nld", // Dutch
            "fre" => "fra", // French
            "gre" => "ell", // Greek
            "per" => "fas", // Persian
            "slo" => "slk", // Slovak
            "wel" => "cym", // Welsh
            "may" => "msa", // Malay
            "rum" => "ron", // Romanian
            "alb" => "sqi", // Albanian
            "arm" => "hye", // Armenian
            "baq" => "eus", // Basque
            "bur" => "mya", // Burmese
            "geo" => "kat", // Georgian
            "ice" => "isl", // Icelandic
            "mac" => "mkd", // Macedonian
            "mao" => "mri", // Maori
            "mon" => "khk", // Mongolian (Cyrillic)
            "nep" => "npi", // Nepali
            "pan" => "pan", // Panjabi
            "scc" => "srp", // Serbian
            "sme" => "sme", // Sami
            "tib" => "bod", // Tibetan
            "wol" => "wol", // Wolof
            _ => threeLetterCode,
        };

        // Determine script from known mappings
        var script = GetScriptForLanguage(iso6393);

        if (script == null)
        {
            return null;
        }

        var floresCode = $"{iso6393}_{script}";

        // Verify this FLORES code exists in our data (check against known prefixes)
        return Iso6391ToFlores.Values.Any(v =>
            v.StartsWith(iso6393, StringComparison.OrdinalIgnoreCase))
            ? floresCode
            : null;
    }

    private static string? GetScriptForLanguage(string iso6393)
    {
        return iso6393 switch
        {
            // Cyrillic
            "rus" or "ukr" or "bul" or "srp" or "bel" or "mkd" or
            "kaz" or "kir" or "tgk" or "khk" or "bak" => "Cyrl",
            // Arabic
            "arb" or "acm" or "acq" or "ajp" or "apc" or "ara" or "ars" or "ary" or "arz" or
            "pbt" or "pes" or "prs" or "ura" or "apd" or "acq" or "acm" => "Arab",
            // Devanagari
            "hin" or "mr" or "ne" or "bho" or "mag" or "mai" => "Deva",
            // East Asian
            "jpn" => "Jpan",
            "zho" => "Hans", // Default to Simplified
            "kor" => "Hang",
            "tha" => "Thai",
            "khm" => "Khmr",
            // Indic
            "ben" => "Beng",
            "guj" => "Gujr",
            "pan" => "Guru",
            "tam" => "Taml",
            "tel" => "Telu",
            "kan" => "Knda",
            "mal" => "Mlym",
            // Other
            "ell" => "Grek",
            "hye" => "Armn",
            "kat" => "Geor",
            "heb" => "Hebr",
            "amh" => "Ethi",
            "mya" => "Mymr",
            "lao" => "Laoo",
            "sin" => "Sinh",
            "bod" => "Tibt",
            "mni" => "Beng",
            // Default to Latin
            _ => "Latn",
        };
    }

    private void EnsureScoresLoaded()
    {
        if (_loaded)
        {
            return;
        }

        lock (_loadLock)
        {
            if (_loaded)
            {
                return;
            }

            if (!File.Exists(_scoresFilePath))
            {
                _nllbScores = new Dictionary<string, double>();
                _loaded = true;
                return;
            }

            var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var lines = File.ReadLines(_scoresFilePath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var parts = line.Split('\t');
                    if (parts.Length < 2)
                    {
                        continue;
                    }

                    // Skip header
                    if (string.Equals(parts[0], "direction", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var score))
                    {
                        scores[parts[0]] = score;
                    }
                }
            }
            catch
            {
                // If file is corrupted, use empty dictionary
            }

            _nllbScores = scores;
            _loaded = true;
        }
    }

    private double? LookupNllbScore(string sourceIso6391, string targetIso6391)
    {
        if (!Iso6391ToFlores.TryGetValue(sourceIso6391, out var sourceFlores) ||
            !Iso6391ToFlores.TryGetValue(targetIso6391, out var targetFlores))
        {
            return null;
        }

        var directionForward = $"{sourceFlores}-{targetFlores}";
        var directionReverse = $"{targetFlores}-{sourceFlores}";

        if (_nllbScores.TryGetValue(directionForward, out var forwardScore))
        {
            return NormalizeSpBleu(forwardScore);
        }

        // Fall back to reverse direction if forward not available
        // (translation quality is often but not always symmetric)
        if (_nllbScores.TryGetValue(directionReverse, out var reverseScore))
        {
            return NormalizeSpBleu(reverseScore) * 0.95; // slight penalty for reverse
        }

        return null;
    }

    private static double NormalizeSpBleu(double spBleu)
    {
        // Map spBLEU range ~2-56 to score range ~10-95
        var normalized = (spBleu / MaxSpBleu) * 100.0;
        return Math.Clamp(normalized, 0, 100);
    }

    private static double GetLlmTier(string languageCode)
    {
        return LlmQualityTiers.TryGetValue(languageCode, out var tier) ? tier : 0.55;
    }

    private static double GetFamilyProximity(string sourceCode, string targetCode)
    {
        var sourceFamily = LanguageFamily.GetValueOrDefault(sourceCode);
        var targetFamily = LanguageFamily.GetValueOrDefault(targetCode);

        if (sourceFamily == null || targetFamily == null)
        {
            return 0.30; // Unknown family — low proximity
        }

        if (sourceFamily != targetFamily)
        {
            // Different families — check for distant relatedness
            return sourceFamily switch
            {
                "indoaryan" when targetFamily is "iranian" => 0.70,
                "iranian" when targetFamily is "indoaryan" => 0.70,
                _ => 0.55, // Distant families
            };
        }

        // Same family — base proximity
        var proximity = 0.85;

        // Sub-family bonus
        if (LanguageSubFamily.TryGetValue(sourceCode, out var sourceSub) &&
            LanguageSubFamily.TryGetValue(targetCode, out var targetSub) &&
            sourceSub == targetSub &&
            SubFamilyBonus.TryGetValue(sourceFamily, out var subBonuses) &&
            subBonuses.TryGetValue(sourceSub, out var bonus))
        {
            proximity += bonus;
        }

        return Math.Min(proximity, 0.95);
    }
}
