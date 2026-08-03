using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Subtitle;

internal enum SubtitleSemanticKind
{
    Dialogue,
    SdhSoundEffect,
    SignOrTitle,
    LyricOrChant,
    ProperNameOnly,
    DrawingOnly,
    SymbolOnly,
    CorruptText
}

internal sealed record SubtitleSemanticClassification(
    SubtitleSemanticKind Kind,
    bool ShouldRequestProvider,
    bool CanPreserveSourceWhenProviderMissing,
    string Reason);

internal static class SubtitleSemanticClassifier
{
    private static readonly HashSet<string> SdhTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "applause", "announcer", "beep", "breathing", "chanting", "cheering", "continues", "coo",
        "crying", "distant", "ethereal", "gasps", "groans", "grumbles", "laughing", "music",
        "playing", "reads", "screaming", "singing", "softly", "song", "whispering"
    };

    private static readonly HashSet<string> StandaloneSfxTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "bang", "bangs", "banging", "bell", "bells", "buzz", "buzzes", "buzzing",
        "clang", "clangs", "clanging", "clatter", "clatters", "clattering", "click", "clicks",
        "clicking", "cough", "coughs", "coughing", "crash", "crashes", "crashing", "creak",
        "creaks", "creaking", "cry", "cries", "door", "doors", "explosion", "explosions",
        "footstep", "footsteps", "giggle", "giggles", "giggling", "gunshot", "gunshots",
        "heartbeat", "heartbeats", "knock", "knocks", "knocking", "laugh", "laughs", "laughter",
        "moan", "moans", "moaning", "roar", "roars", "roaring", "rustle", "rustles", "rustling",
        "sigh", "sighs", "sighing", "slam", "slams", "slamming", "sneeze", "sneezes", "sneezing",
        "sniff", "sniffs", "sniffing", "sniffles", "sob", "sobs", "sobbing", "squeak", "squeaks",
        "squeaking", "stomp", "stomps", "stomping", "thud", "thuds", "thunder", "thunderclap",
        "whistle", "whistles", "whistling", "wind"
    };

    private static readonly HashSet<string> SdhCueModifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "approaching", "away", "distant", "echo", "echoes", "echoing", "faint", "loud", "loudly",
        "nearby", "off-screen", "offscreen", "outside", "receding", "slow", "slowly", "soft",
        "sudden", "suddenly"
    };

    private static readonly HashSet<string> LyricOrChantTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "fond", "embrace", "ha", "ha'aheo", "haaheo", "i", "ka", "lipo", "na", "nani", "noho",
        "omoi", "kokoro", "tsuyoku", "kedo"
    };

    private static readonly HashSet<string> SignStyleTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "sign", "title", "text", "onscreen", "on-screen"
    };

    private static readonly HashSet<string> NonNameUnchangedTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "ah", "bye", "go", "ha", "hey", "hi", "hmm", "hello", "help", "nah", "no",
        "oh", "ok", "okay", "please", "run", "shh", "sorry", "stop", "thanks", "thank",
        "uh", "um", "wait", "whoa", "yeah", "yep", "yes"
    };

    private static readonly HashSet<string> LyricStyleTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "karaoke", "kfx", "lyric", "lyrics", "op", "ed", "rom", "song"
    };

    public static SubtitleSemanticClassification Classify(
        SubtitleItem? subtitle,
        string providerText,
        string? styleName = null)
    {
        var text = SubtitleTextStructure.NormalizeProviderTranslationText(providerText).Trim();
        var style = styleName ?? subtitle?.SsaDialogue?.Style;

        if (string.IsNullOrWhiteSpace(text))
        {
            var kind = HasDrawingCommands(subtitle)
                ? SubtitleSemanticKind.DrawingOnly
                : SubtitleSemanticKind.SymbolOnly;
            return new SubtitleSemanticClassification(kind, false, true, ToReason(kind));
        }

        if (SubtitleFormatterService.IsMeaningless(text) || !text.Any(char.IsLetterOrDigit))
        {
            return new SubtitleSemanticClassification(
                SubtitleSemanticKind.SymbolOnly,
                false,
                true,
                ToReason(SubtitleSemanticKind.SymbolOnly));
        }

        if (IsLikelyCorruptText(text))
        {
            return new SubtitleSemanticClassification(
                SubtitleSemanticKind.CorruptText,
                true,
                false,
                ToReason(SubtitleSemanticKind.CorruptText));
        }

        if (IsSdhSoundEffect(text))
        {
            return new SubtitleSemanticClassification(
                SubtitleSemanticKind.SdhSoundEffect,
                true,
                true,
                ToReason(SubtitleSemanticKind.SdhSoundEffect));
        }

        if (IsLyricOrChant(text, style))
        {
            return new SubtitleSemanticClassification(
                SubtitleSemanticKind.LyricOrChant,
                true,
                true,
                ToReason(SubtitleSemanticKind.LyricOrChant));
        }

        if (IsSignOrTitle(text, style))
        {
            return new SubtitleSemanticClassification(
                SubtitleSemanticKind.SignOrTitle,
                true,
                true,
                ToReason(SubtitleSemanticKind.SignOrTitle));
        }

        if (IsLikelyProperNameOnlyCue(text))
        {
            return new SubtitleSemanticClassification(
                SubtitleSemanticKind.ProperNameOnly,
                true,
                true,
                ToReason(SubtitleSemanticKind.ProperNameOnly));
        }

        return new SubtitleSemanticClassification(
            SubtitleSemanticKind.Dialogue,
            true,
            false,
            ToReason(SubtitleSemanticKind.Dialogue));
    }

    public static bool CanIgnoreUnchangedEcho(string text)
    {
        return CanIgnoreUnchangedEcho(text, null);
    }

    public static bool CanIgnoreUnchangedEcho(
        string text,
        SubtitleItem? subtitle,
        string? styleName = null)
    {
        var classification = Classify(subtitle, text, styleName);
        return IsSourcePreservable(classification);
    }

    public static bool IsSafeSourceEcho(
        SubtitleItem? subtitle,
        string sourceText,
        string? translatedText,
        string? styleName = null)
    {
        if (string.IsNullOrWhiteSpace(sourceText) || string.IsNullOrWhiteSpace(translatedText))
        {
            return false;
        }

        var normalizedSource = NormalizeSourceEchoText(sourceText);
        var normalizedTranslation = NormalizeSourceEchoText(translatedText);
        return string.Equals(normalizedSource, normalizedTranslation, StringComparison.Ordinal) &&
               CanIgnoreUnchangedEcho(normalizedSource, subtitle, styleName);
    }

    private static bool IsSourcePreservable(SubtitleSemanticClassification classification)
    {
        return classification.Kind is SubtitleSemanticKind.SdhSoundEffect
            or SubtitleSemanticKind.LyricOrChant
            or SubtitleSemanticKind.SignOrTitle
            or SubtitleSemanticKind.ProperNameOnly;
    }

    private static string NormalizeSourceEchoText(string text)
    {
        var normalized = SubtitleTextStructure.NormalizeProviderTranslationText(text)
            .Replace("\\N", " ", StringComparison.Ordinal)
            .Replace("\\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Trim();
        return string.Join(' ', normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static bool IsLikelyCorruptText(string text)
    {
        var normalized = SubtitleTextStructure.NormalizeProviderTranslationText(text).Trim();
        if (normalized.Length < 8)
        {
            return false;
        }

        if (normalized.Count(c => c is '[' or '(') != normalized.Count(c => c is ']' or ')') &&
            normalized.Length >= 16)
        {
            return true;
        }

        if (HasRepeatedCharacterPattern(normalized))
        {
            return true;
        }

        var tokens = GetAlphaNumericTokens(normalized).ToList();
        if (tokens.Count == 0)
        {
            return false;
        }

        var suspiciousTokens = tokens.Count(IsSuspiciousToken);
        if (suspiciousTokens >= 2)
        {
            return true;
        }

        if (tokens.Count == 1 && IsSuspiciousToken(tokens[0]))
        {
            return true;
        }

        var letters = normalized.Count(char.IsLetter);
        var spaces = normalized.Count(char.IsWhiteSpace);
        if (letters >= 18 && spaces == 0 && suspiciousTokens > 0)
        {
            return true;
        }

        if (HasMixedScriptGarbage(normalized))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Detects repeated character patterns common in OCR garbage output,
    /// e.g. "AAAAA BBBBB" or "11111 22222" where a single character dominates a token.
    /// </summary>
    private static bool HasRepeatedCharacterPattern(string text)
    {
        if (text.Length < 4)
        {
            return false;
        }

        var tokens = GetAlphaNumericTokens(text).ToList();
        if (tokens.Count == 0)
        {
            return false;
        }

        var repeatedCharTokens = 0;
        foreach (var token in tokens)
        {
            if (token.Length < 3)
            {
                continue;
            }

            var charGroups = token.GroupBy(c => c).ToList();
            if (charGroups.Count == 0)
            {
                continue;
            }

            var maxGroupSize = charGroups.Max(g => g.Count());
            if (maxGroupSize >= 3 && (double)maxGroupSize / token.Length >= 0.6)
            {
                repeatedCharTokens++;
            }
        }

        return repeatedCharTokens >= 2;
    }

    /// <summary>
    /// Detects mixed-script garbage where a single line combines characters from
    /// incompatible scripts (e.g., Latin + CJK + Cyrillic), which is a strong OCR error signal.
    /// </summary>
    private static bool HasMixedScriptGarbage(string text)
    {
        if (text.Length < 6)
        {
            return false;
        }

        var hasLatin = false;
        var hasCjk = false;
        var hasOtherNonLatin = false;

        foreach (var c in text)
        {
            if (!char.IsLetter(c))
            {
                continue;
            }

            var category = char.GetUnicodeCategory(c);
            if (category == System.Globalization.UnicodeCategory.OtherLetter)
            {
                // CJK, Hangul, Thai, etc.
                hasCjk = true;
            }
            else if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
            {
                hasLatin = true;
            }
            else
            {
                // Cyrillic, Arabic, Devanagari, etc.
                hasOtherNonLatin = true;
            }
        }

        // More than 2 script categories in one line is suspicious for OCR
        var scriptCount = (hasLatin ? 1 : 0) + (hasCjk ? 1 : 0) + (hasOtherNonLatin ? 1 : 0);
        return scriptCount >= 3;
    }

    public static string ToReason(SubtitleSemanticKind kind)
    {
        return kind switch
        {
            SubtitleSemanticKind.SdhSoundEffect => "sdh-sound-effect",
            SubtitleSemanticKind.SignOrTitle => "sign-or-title",
            SubtitleSemanticKind.LyricOrChant => "lyric-or-chant",
            SubtitleSemanticKind.ProperNameOnly => "proper-name-only",
            SubtitleSemanticKind.DrawingOnly => "drawing-only",
            SubtitleSemanticKind.SymbolOnly => "symbol-only",
            SubtitleSemanticKind.CorruptText => "corrupt-text",
            _ => "dialogue"
        };
    }

    private static bool IsSdhSoundEffect(string text)
    {
        var trimmed = text.Trim();
        var bracketed = trimmed.Length >= 2 &&
                        ((trimmed[0] == '[' && trimmed[^1] == ']') ||
                         (trimmed[0] == '(' && trimmed[^1] == ')') ||
                         (trimmed[0] == '（' && trimmed[^1] == '）'));
        if (!bracketed)
        {
            return false;
        }

        if (trimmed.Any(c => c is '声' or '音') ||
            trimmed.Contains("歓声", StringComparison.Ordinal) ||
            trimmed.Contains("ざわめき", StringComparison.Ordinal))
        {
            return true;
        }

        var tokens = GetWordTokens(trimmed).ToList();
        if (tokens.Any(token => SdhTerms.Contains(token)))
        {
            return true;
        }

        return tokens.Count > 0 &&
               tokens.Any(token => StandaloneSfxTerms.Contains(token)) &&
               tokens.All(token => StandaloneSfxTerms.Contains(token) || SdhCueModifiers.Contains(token));
    }

    private static bool IsSignOrTitle(string text, string? style)
    {
        if (ContainsStyleTerm(style, SignStyleTerms))
        {
            return true;
        }

        var trimmed = text.Trim();
        if (trimmed.Length is < 3 or > 48 || trimmed.Contains('\n'))
        {
            return false;
        }

        var words = GetWordTokens(trimmed).ToList();
        return words.Count is >= 1 and <= 4 &&
               trimmed.Contains('-', StringComparison.Ordinal) &&
               trimmed.Any(char.IsUpper) &&
               !trimmed.EndsWith(".", StringComparison.Ordinal) &&
               !trimmed.EndsWith("?", StringComparison.Ordinal) &&
               !trimmed.EndsWith("!", StringComparison.Ordinal);
    }

    private static bool IsLyricOrChant(string text, string? style)
    {
        if (text.Contains('♪') || text.Contains('♫') || ContainsStyleTerm(style, LyricStyleTerms))
        {
            return true;
        }

        var tokens = GetWordTokens(text).ToList();
        if (tokens.Count == 0)
        {
            return false;
        }

        var lyricTokenCount = tokens.Count(token => LyricOrChantTerms.Contains(token));
        if (lyricTokenCount >= 2)
        {
            return true;
        }

        return text.Contains("fond embrace", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("nani", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyProperNameOnlyCue(string text)
    {
        var candidate = StripSpeakerPrefix(text);
        var tokens = GetOriginalWordTokens(candidate).ToList();
        if (tokens.Count == 0 || tokens.Count > 12)
        {
            return false;
        }

        var distinctTokens = tokens
            .Select(token => token.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (tokens.Count > 8 && distinctTokens > 3)
        {
            return false;
        }

        return tokens.All(token => IsNameLikeToken(token, allowLongUppercase: false));
    }

    private static string StripSpeakerPrefix(string text)
    {
        var normalized = SubtitleTextStructure.NormalizeProviderTranslationText(text)
            .Replace("\\N", " ", StringComparison.Ordinal)
            .Replace("\\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Trim();
        var trimmed = TrimLeadingCueMarkers(normalized);
        var colonIndex = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (colonIndex <= 0 || colonIndex > 40)
        {
            return trimmed;
        }

        var prefix = trimmed[..colonIndex];
        var suffix = trimmed[(colonIndex + 1)..].Trim();
        if (suffix.Length == 0 || !LooksLikeSpeakerLabel(prefix))
        {
            return trimmed;
        }

        return TrimLeadingCueMarkers(suffix);
    }

    private static string TrimLeadingCueMarkers(string text)
    {
        return text.Trim().TrimStart('-').Trim();
    }

    private static bool LooksLikeSpeakerLabel(string text)
    {
        var tokens = GetOriginalWordTokens(text).ToList();
        return tokens.Count is >= 1 and <= 4 &&
               tokens.All(token => IsNameLikeToken(token, allowLongUppercase: true));
    }

    private static bool IsNameLikeToken(string token, bool allowLongUppercase)
    {
        var trimmed = token.Trim('\'', '-');
        if (trimmed.Length < 2 || trimmed.All(char.IsDigit))
        {
            return false;
        }

        if (NonNameUnchangedTerms.Contains(trimmed))
        {
            return false;
        }

        if (trimmed.Contains('-', StringComparison.Ordinal))
        {
            var parts = trimmed.Split('-', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 1 && parts.All(part => IsNameLikeToken(part, allowLongUppercase));
        }

        if (trimmed.All(c => !char.IsLetter(c) || char.IsUpper(c)))
        {
            return allowLongUppercase || trimmed.Length <= 5;
        }

        return char.IsUpper(trimmed[0]) && trimmed.Skip(1).Any(char.IsLower);
    }

    private static IEnumerable<string> GetOriginalWordTokens(string text)
    {
        var start = -1;
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsLetterOrDigit(text[index]) || text[index] is '\'' or '-')
            {
                if (start < 0)
                {
                    start = index;
                }

                continue;
            }

            if (start >= 0)
            {
                var token = text[start..index].Trim('\'', '-');
                if (token.Length > 0)
                {
                    yield return token;
                }

                start = -1;
            }
        }

        if (start >= 0)
        {
            var token = text[start..].Trim('\'', '-');
            if (token.Length > 0)
            {
                yield return token;
            }
        }
    }

    private static bool ContainsStyleTerm(string? style, HashSet<string> terms)
    {
        if (string.IsNullOrWhiteSpace(style))
        {
            return false;
        }

        return terms.Any(term => style.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasDrawingCommands(SubtitleItem? subtitle)
    {
        return subtitle?.Lines.Any(line =>
            line.Contains(@"{\p", StringComparison.OrdinalIgnoreCase) ||
            line.Contains(@"\p1", StringComparison.OrdinalIgnoreCase) ||
            line.Contains(@"\p2", StringComparison.OrdinalIgnoreCase) ||
            line.Contains(@"\p3", StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static IEnumerable<string> GetWordTokens(string text)
    {
        return GetAlphaNumericTokens(text)
            .Select(token => token.Trim('\'', '-'))
            .Where(token => token.Length > 0);
    }

    private static IEnumerable<string> GetAlphaNumericTokens(string text)
    {
        var start = -1;
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsLetterOrDigit(text[index]) || text[index] is '\'' or '-')
            {
                if (start < 0)
                {
                    start = index;
                }

                continue;
            }

            if (start >= 0)
            {
                yield return text[start..index].ToLowerInvariant();
                start = -1;
            }
        }

        if (start >= 0)
        {
            yield return text[start..].ToLowerInvariant();
        }
    }

    private static bool IsSuspiciousToken(string token)
    {
        var letters = token.Count(char.IsLetter);
        if (letters < 10)
        {
            return false;
        }

        var vowelCount = token.Count(c => c is 'a' or 'e' or 'i' or 'o' or 'u');
        var vowelRatio = letters == 0 ? 0 : (double)vowelCount / letters;
        return vowelRatio < 0.25 || letters >= 18;
    }
}
