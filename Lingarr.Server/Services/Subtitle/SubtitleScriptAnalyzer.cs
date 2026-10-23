using System.Text;

namespace Lingarr.Server.Services.Subtitle;

/// <summary>
/// Unicode script families used by subtitle language contradiction detection.
/// </summary>
public enum SubtitleScriptFamily
{
    Unknown,
    Latin,
    Han,
    Kana,
    Hangul,
    Cyrillic,
    Greek,
    Arabic,
    Hebrew,
    Devanagari,
    Thai,
    Armenian,
    Georgian
}

/// <summary>
/// Result of a deterministic subtitle language contradiction check.
/// </summary>
public enum SubtitleLanguageVerdict
{
    /// <summary>Not enough readable text to make any claim.</summary>
    Insufficient,

    /// <summary>The sampled text is consistent with the declared tag.</summary>
    Consistent,

    /// <summary>The sampled text clearly contradicts the declared tag and a language was resolved.</summary>
    Contradiction,

    /// <summary>The script contradicts the tag but the exact language cannot be resolved deterministically.</summary>
    AmbiguousScriptMatch
}

public sealed record SubtitleLanguageScriptResult(
    SubtitleLanguageVerdict Verdict,
    string? DetectedLanguage,
    double Confidence,
    string Method,
    string Evidence);

/// <summary>
/// Deterministic script and function word analysis for subtitle samples. No I/O, no LLM.
/// Language detection here is deliberately conservative: it only reports a contradiction
/// when the dominant Unicode script family is impossible for the declared language, or when
/// function word evidence for another language is overwhelming. Short or mixed samples are
/// reported as insufficient/ambiguous instead of guessing.
/// </summary>
public static class SubtitleScriptAnalyzer
{
    /// <summary>
    /// Minimum letters required before any cross-script contradiction is reported.
    /// </summary>
    public const int MinimumCrossScriptLetters = 60;

    /// <summary>
    /// Minimum letters required before a same-script (e.g. Latin to Latin) override is reported.
    /// </summary>
    public const int MinimumSameScriptLetters = 150;

    /// <summary>
    /// Minimum number of cues that must contain readable letters.
    /// </summary>
    public const int MinimumReadableCues = 2;

    private const double MinimumDominantScriptShare = 0.85;
    private const double MinimumDetectedLanguageConfidence = 0.90;
    private const double MinimumCrossScriptStopwordScore = 0.50;
    private const double MinimumSameScriptStopwordScore = 0.60;
    private const double MinimumSameScriptMargin = 0.25;
    private const double MaximumSameScriptDeclaredScore = 0.10;
    private const double MinimumSameScriptStrongMargin = 0.40;

    private sealed record LanguageProfile(
        string Code,
        SubtitleScriptFamily Script,
        IReadOnlySet<string> FunctionWords);

    private static readonly Dictionary<SubtitleScriptFamily, string> UniqueScriptLanguages = new()
    {
        [SubtitleScriptFamily.Kana] = "ja",
        [SubtitleScriptFamily.Hangul] = "ko",
        [SubtitleScriptFamily.Greek] = "el",
        [SubtitleScriptFamily.Hebrew] = "he",
        [SubtitleScriptFamily.Devanagari] = "hi",
        [SubtitleScriptFamily.Thai] = "th",
        [SubtitleScriptFamily.Armenian] = "hy",
        [SubtitleScriptFamily.Georgian] = "ka"
    };

    private static readonly IReadOnlyDictionary<string, SubtitleScriptFamily> LanguageScripts =
        new Dictionary<string, SubtitleScriptFamily>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = SubtitleScriptFamily.Latin,
            ["es"] = SubtitleScriptFamily.Latin,
            ["fr"] = SubtitleScriptFamily.Latin,
            ["de"] = SubtitleScriptFamily.Latin,
            ["it"] = SubtitleScriptFamily.Latin,
            ["pt"] = SubtitleScriptFamily.Latin,
            ["nl"] = SubtitleScriptFamily.Latin,
            ["pl"] = SubtitleScriptFamily.Latin,
            ["ro"] = SubtitleScriptFamily.Latin,
            ["tr"] = SubtitleScriptFamily.Latin,
            ["sv"] = SubtitleScriptFamily.Latin,
            ["da"] = SubtitleScriptFamily.Latin,
            ["no"] = SubtitleScriptFamily.Latin,
            ["fi"] = SubtitleScriptFamily.Latin,
            ["cs"] = SubtitleScriptFamily.Latin,
            ["hu"] = SubtitleScriptFamily.Latin,
            ["id"] = SubtitleScriptFamily.Latin,
            ["vi"] = SubtitleScriptFamily.Latin,
            ["ru"] = SubtitleScriptFamily.Cyrillic,
            ["uk"] = SubtitleScriptFamily.Cyrillic,
            ["bg"] = SubtitleScriptFamily.Cyrillic,
            ["el"] = SubtitleScriptFamily.Greek,
            ["ar"] = SubtitleScriptFamily.Arabic,
            ["he"] = SubtitleScriptFamily.Hebrew,
            ["hi"] = SubtitleScriptFamily.Devanagari,
            ["th"] = SubtitleScriptFamily.Thai,
            ["ja"] = SubtitleScriptFamily.Han,
            ["zh"] = SubtitleScriptFamily.Han,
            ["ko"] = SubtitleScriptFamily.Hangul
        };

    private static readonly LanguageProfile[] LatinProfiles = BuildProfiles(
        SubtitleScriptFamily.Latin,
        ("en", "the a an and or but if of to in is are was were it this that you i he she we they not no yes do does did be have has with for on at as my your his her our their what who why how there here can will would should could just very so all"),
        ("es", "el la los las un una de del que y en es esta estan son no si por para con se su sus mi tu al lo como mas pero este esta estos estas hay muy yo tu el ella nosotros ellos que como cuando donde porque todo nada bien"),
        ("fr", "le la les un une des de du que qui et en est sont pas ne je tu il elle nous vous ils elles ce cette ces mon ton son notre votre leur pour avec dans sur plus mais ou quand comment pourquoi tout rien bien tres si"),
        ("de", "der die das den dem des ein eine einen einem einer und oder aber ich du er sie es wir ihr nicht kein mit von zu im in ist sind war waren habe hat auf fur aus bei nach uber unter wenn weil dass wie was wer wo ja nein sehr nur"),
        ("it", "il lo la i gli le un una di del della che e in e non si per con su tra fra questo questa questi queste sono era erano ho hai ha abbiamo avete hanno molto poco anche ma come perche quando dove chi cosa"),
        ("pt", "o a os as um uma de do da dos das que e em no na nao sim por para com se seu sua meus minhas meu minha ao como mais mas este esta estes estas ha muito eu tu ele ela nos eles elas o que quando onde porque"),
        ("nl", "de het een en of maar ik jij hij zij wij jullie niet geen met van te in is zijn was waren heb hebt heeft op voor uit bij naar over onder als omdat dat wie wat waar ja nee zeer alleen"),
        ("pl", "i w nie na ze sie to jest sa byc byl byla bylo do z od dla ale lub tak nie mam masz ma mamy macie maja bardzo tylko juz jeszcze sie co jak gdzie dlaczego kto moj twoj jego jej nasz wasz ich"),
        ("ro", "si in la de din cu pe nu un o este sunt era erau am ai are avem aveti au foarte mai dar sau daca cum unde de ce cine meu mea tau ta lui ei nostru vostru lor acest aceasta acesti aceste"),
        ("tr", "ve bir bu su o da de ki ile icin gibi kadar ama fakat veya cok daha en ne nasil neden nerede zaman ben sen biz siz onlar degil var yok mi mu"),
        ("sv", "och i att det som en ett ar var varit jag du han hon vi ni de inte med av till for pa fran om men eller sa mycket bara redan hur varfor vem vad"),
        ("da", "og i at det som en et er var var jeg du han hun vi I de ikke med af til for pa fra om men eller sa meget kun allerede hvordan hvorfor hvem hvad"),
        ("no", "og i at det som en et er var var jeg du han hun vi dere de ikke med av til for pa fra om men eller sa mye bare allerede hvordan hvorfor hvem hva"),
        ("fi", "ja on ei se etta mina sina me te he olemme olette ovat oli olivat mutta tai kun missa miksi kuka mika kuinka paljon vain jo nyt"),
        ("cs", "a v na se je jsou byl byla bylo do z od pro ale nebo tak ne mam mas ma mame mate maji velmi jen uz jeste co jak kde proc kdo muj tvuj jeho jeji nas vas jejich"),
        ("hu", "a az es hogy nem egy van volt voltak vagy de ha meg csak mar is nagyon mit hogyan miert ki hol en te o mi ti ok"),
        ("id", "dan di ke dari yang ini itu tidak bukan saya kamu dia kami kita mereka dengan untuk pada dari karena jika atau tetapi sangat sudah masih bagaimana mengapa siapa apa"),
        ("vi", "va la cua co khong duoc trong cho mot nhung cac voi de khi thi nguoi nay do toi ban anh chi em chung rat da dang se"));

    private static readonly LanguageProfile[] CyrillicProfiles = BuildProfiles(
        SubtitleScriptFamily.Cyrillic,
        ("ru", "ÄÂ¸ ÄË› ÄËťÄÂµ ÄËťÄÂ° Ĺâ€ˇĹâ€šÄÄľ ĹĹą ĹÂ ÄÄľÄËť ÄÄľÄËťÄÂ° ĹĹ¤Ĺâ€šÄÄľ ÄĹźÄÂ°ÄĹź ÄÄ˝Ĺâ€ą ÄË›Ĺâ€ą ÄÄľÄËťÄÂ¸ ÄËťÄÄľ ÄÂ´ÄÂ° ÄËťÄÂµĹâ€š ÄĹĽÄÄľ ÄÂ·ÄÂ° ÄÂ¸ÄÂ· ÄÄľĹâ€š ÄÂ´ÄÂ»ĹĹą ÄÂ±Ĺâ€ąÄÂ» ÄÂ±Ĺâ€ąÄÂ»ÄÂ° ÄÂ±Ĺâ€ąÄÂ»ÄÄľ ÄÂµĹÂĹâ€šĹĹš Ĺâ€šÄÄľÄÂ»ĹĹšÄĹźÄÄľ ĹÂÄÂ¶ÄÂµ ÄÂµĹâ€°ÄÂµ ÄÄľĹâ€ˇÄÂµÄËťĹĹš Ĺâ€šÄÂ°ÄĹź ÄÂ¶ÄÂµ ÄÂ»ÄÂ¸ ÄÂ±Ĺâ€ą Ĺâ€šÄÄľ ÄË›ĹÂÄÂµ ÄĹźÄÄľÄĹ‚ÄÂ´ÄÂ° ÄĹ‚ÄÂ´ÄÂµ ÄĹĽÄÄľĹâ€ˇÄÂµÄÄ˝ĹÂ ÄĹźĹâ€šÄÄľ ÄÄ˝ÄÄľÄÄ… Ĺâ€šÄË›ÄÄľÄÄ… ÄÂµÄĹ‚ÄÄľ ÄÂµÄÂµ ÄËťÄÂ°ĹÂ ÄË›ÄÂ°ĹÂ ÄÂ¸Ĺâ€¦"),
        ("uk", "Ĺâ€“ ÄÄ… ÄË› ĹÂ ÄËťÄÂµ ÄËťÄÂ° Ĺâ€°ÄÄľ ĹĹą ÄÂ· ÄË›Ĺâ€“ÄËť ÄË›ÄÄľÄËťÄÂ° Ĺâ€ ÄÂµ ĹĹąÄĹź ÄÄ˝ÄÂ¸ ÄË›ÄÂ¸ ÄË›ÄÄľÄËťÄÂ¸ ÄÂ°ÄÂ»ÄÂµ Ĺâ€šÄÂ°ÄĹź ÄËťĹâ€“ ÄĹĽÄÄľ Ĺâ€“ÄÂ· ÄË›Ĺâ€“ÄÂ´ ÄÂ´ÄÂ»ĹĹą ÄÂ±ĹÂÄË› ÄÂ±ĹÂÄÂ»ÄÂ° ÄÂ±ĹÂÄÂ»ÄÄľ Ĺâ€ť Ĺâ€šĹâ€“ÄÂ»ĹĹšÄĹźÄÂ¸ ÄË›ÄÂ¶ÄÂµ Ĺâ€°ÄÂµ ÄÂ´ĹÂÄÂ¶ÄÂµ Ĺâ€ˇÄÂ¸ Ĺâ€šÄÄľ ÄË›ĹÂĹâ€“ ÄĹźÄÄľÄÂ»ÄÂ¸ ÄÂ´ÄÂµ Ĺâ€ˇÄÄľÄÄ˝ĹÂ Ĺâ€¦Ĺâ€šÄÄľ ÄÄ˝Ĺâ€“ÄÄ… Ĺâ€šÄË›Ĺâ€“ÄÄ… ÄÄ…ÄÄľÄĹ‚ÄÄľ Ĺâ€”Ĺâ€” ÄËťÄÂ°ĹÂ ÄË›ÄÂ°ĹÂ Ĺâ€”Ĺâ€¦"),
        ("bg", "ÄÂ¸ ÄË› ÄËťÄÂµ ÄËťÄÂ° ÄĹźÄÂ°ÄĹźÄË›ÄÄľ ÄÂ°ÄÂ· ĹÂ Ĺâ€šÄÄľÄÄ… Ĺâ€šĹĹą Ĺâ€šÄÄľÄË›ÄÂ° ÄĹźÄÂ°ÄĹź ÄËťÄÂ¸ÄÂµ ÄË›ÄÂ¸ÄÂµ Ĺâ€šÄÂµ ÄËťÄÄľ ÄÂ´ÄÂ° ÄËťÄÂµ ÄĹĽÄÄľ ÄÂ·ÄÂ° ÄÄľĹâ€š ÄÂ±ÄÂ¸ÄÂ» ÄÂ±ÄÂ¸ÄÂ»ÄÂ° ÄÂ±ÄÂ¸ÄÂ»ÄÄľ ÄÂµ ĹÂÄÂ°ÄÄ˝ÄÄľ ÄÄľĹâ€°ÄÂµ ÄË›ÄÂµĹâ€ˇÄÂµ ÄÄ˝ÄËťÄÄľÄĹ‚ÄÄľ Ĺâ€šÄÂ°ÄĹźÄÂ° ÄÂ»ÄÂ¸ ÄÂ±ÄÂ¸ Ĺâ€šÄÄľ ÄË›ĹÂÄÂ¸Ĺâ€ˇÄĹźÄÂ¸ ÄĹźÄÄľÄĹ‚ÄÂ°Ĺâ€šÄÄľ ÄĹźĹĹ ÄÂ´ÄÂµ ÄÂ·ÄÂ°Ĺâ€°ÄÄľ ÄĹźÄÄľÄÄ… ÄÄ˝ÄÄľÄÄ… Ĺâ€šÄË›ÄÄľÄÄ… ÄËťÄÂµÄĹ‚ÄÄľÄË› ÄËťÄÂµÄÄ…ÄËťÄÂ¸ĹĹą ÄËťÄÂ°ĹÂ ÄË›ÄÂ°ĹÂ Ĺâ€šÄÂµĹâ€¦ÄÂµÄËť"));

    private static readonly IReadOnlyList<LanguageProfile> AllProfiles =
        LatinProfiles.Concat(CyrillicProfiles).ToList();

    private static readonly IReadOnlyDictionary<string, LanguageProfile> ProfilesByCode =
        AllProfiles.ToDictionary(profile => profile.Code, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Normalizes a declared language code (2 or 3 letter, optional region) to the language
    /// profile keys used here. Returns null when the language is not modeled.
    /// </summary>
    public static string? ResolveLanguageCode(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        if (!SubtitleLanguageHelper.TryNormalizeKnownLanguageCode(language, out var normalized))
        {
            return null;
        }

        return LanguageScripts.ContainsKey(normalized) ? normalized : null;
    }

    /// <summary>
    /// Runs the deterministic contradiction check over text samples taken from a subtitle
    /// track. The caller decides sampling density; each entry is one cue text.
    /// </summary>
    public static SubtitleLanguageScriptResult Detect(
        string? declaredLanguage,
        IReadOnlyList<string> cueSamples)
    {
        var declared = ResolveLanguageCode(declaredLanguage);
        var profile = CountScripts(cueSamples);
        var readableCues = cueSamples.Count(cue => CountLetters(cue) >= 3);

        if (declared == null)
        {
            return new SubtitleLanguageScriptResult(
                SubtitleLanguageVerdict.Insufficient,
                null,
                0,
                "declared-language-unsupported",
                $"Declared language '{declaredLanguage ?? "unknown"}' is not modeled for contradiction detection.");
        }

        if (profile.TotalLetters < MinimumCrossScriptLetters || readableCues < MinimumReadableCues)
        {
            return new SubtitleLanguageScriptResult(
                SubtitleLanguageVerdict.Insufficient,
                null,
                0,
                "insufficient-sample",
                $"Only {profile.TotalLetters} letters across {readableCues} readable cues; " +
                $"at least {MinimumCrossScriptLetters} letters across {MinimumReadableCues} cues are required.");
        }

        var declaredScript = LanguageScripts[declared];
        var dominant = profile.DominantScript;
        var dominantShare = dominant == SubtitleScriptFamily.Unknown
            ? 0
            : (double)profile.GroupedLettersByScript[dominant] / profile.TotalLetters;

        if (dominantShare < MinimumDominantScriptShare)
        {
            return new SubtitleLanguageScriptResult(
                SubtitleLanguageVerdict.Insufficient,
                null,
                0,
                "mixed-script-sample",
                $"Dominant script {dominant} covers only {dominantShare:P0} of letters; mixed samples are not judged.");
        }

        if (ScriptMatchesDeclared(declaredScript, dominant))
        {
            return CheckSameScript(declared, cueSamples, profile, dominantShare);
        }

        var resolved = ResolveContradictedLanguage(declared, dominant, profile, cueSamples);
        if (resolved == null)
        {
            return new SubtitleLanguageScriptResult(
                SubtitleLanguageVerdict.AmbiguousScriptMatch,
                null,
                0,
                "script-mismatch-ambiguous",
                $"Declared '{declared}' expects {declaredScript} but {dominantShare:P0} of letters are {dominant}; " +
                "the exact language could not be resolved deterministically.");
        }

        return resolved;
    }

    private static bool ScriptMatchesDeclared(SubtitleScriptFamily declared, SubtitleScriptFamily observed)
    {
        if (declared == observed)
        {
            return true;
        }

        // Japanese uses Han plus kana; Korean commonly mixes Hangul with Han; Chinese is Han.
        if (declared == SubtitleScriptFamily.Han)
        {
            return observed is SubtitleScriptFamily.Han or SubtitleScriptFamily.Kana;
        }

        if (declared == SubtitleScriptFamily.Hangul)
        {
            return observed is SubtitleScriptFamily.Hangul or SubtitleScriptFamily.Han;
        }

        return false;
    }

    private static SubtitleLanguageScriptResult CheckSameScript(
        string declared,
        IReadOnlyList<string> cueSamples,
        ScriptProfile profile,
        double dominantShare)
    {
        // Latin texts can only be contradicted by overwhelming function-word evidence,
        // because names, loan words and short lines routinely look foreign.
        if (LanguageScripts[declared] != SubtitleScriptFamily.Latin)
        {
            return new SubtitleLanguageScriptResult(
                SubtitleLanguageVerdict.Consistent,
                null,
                0.99,
                "script-ratio",
                $"Declared '{declared}' matches the sampled script ({dominantShare:P0}).");
        }

        if (profile.TotalLetters < MinimumSameScriptLetters)
        {
            return new SubtitleLanguageScriptResult(
                SubtitleLanguageVerdict.Consistent,
                null,
                0.99,
                "script-ratio",
                $"Declared '{declared}' matches the sampled script; sample too small for a cross-language override.");
        }

        var scores = ScoreProfiles(cueSamples, LatinProfiles);
        var declaredScore = scores.TryGetValue(declared, out var ownScore) ? ownScore : 0;
        var best = scores
            .Where(pair => !string.Equals(pair.Key, declared, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .FirstOrDefault();

        if (best.Key == null ||
            best.Value < MinimumSameScriptStopwordScore ||
            best.Value - declaredScore < MinimumSameScriptMargin ||
            (declaredScore > MaximumSameScriptDeclaredScore &&
             best.Value - declaredScore < MinimumSameScriptStrongMargin))
        {
            return new SubtitleLanguageScriptResult(
                SubtitleLanguageVerdict.Consistent,
                null,
                0.99,
                "script-ratio+stopwords",
                $"Declared '{declared}' matches the sampled script and function word evidence " +
                $"(declared score {declaredScore:F2}, best other {(best.Key == null ? "none" : $"{best.Key} {best.Value:F2}")}).");
        }

        var confidence = Math.Min(0.95, 0.90 + (best.Value - MinimumSameScriptStopwordScore));
        return new SubtitleLanguageScriptResult(
            SubtitleLanguageVerdict.Contradiction,
            best.Key,
            confidence,
            "stopwords-same-script",
            $"Declared '{declared}' scores {declaredScore:F2} but sampled text scores {best.Value:F2} " +
            $"for '{best.Key}' across {profile.TotalLetters} letters.");
    }

    private static SubtitleLanguageScriptResult? ResolveContradictedLanguage(
        string declared,
        SubtitleScriptFamily dominant,
        ScriptProfile profile,
        IReadOnlyList<string> cueSamples)
    {
        if (dominant == SubtitleScriptFamily.Latin)
        {
            var scores = ScoreProfiles(cueSamples, LatinProfiles);
            var best = scores
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .FirstOrDefault();

            if (best.Key == null || best.Value < MinimumCrossScriptStopwordScore)
            {
                return null;
            }

            var confidence = Math.Min(0.98, 0.90 + (best.Value - MinimumCrossScriptStopwordScore));
            return new SubtitleLanguageScriptResult(
                SubtitleLanguageVerdict.Contradiction,
                best.Key,
                confidence,
                "script-ratio+stopwords",
                $"Declared '{declared}' expects {LanguageScripts[declared]} but " +
                $"{(double)profile.LettersByScript[SubtitleScriptFamily.Latin] / profile.TotalLetters:P0} of letters are Latin; " +
                $"function word evidence resolves '{best.Key}' (score {best.Value:F2}, {profile.TotalLetters} letters).");
        }

        if (dominant == SubtitleScriptFamily.Han)
        {
            // Han-only text can be Chinese or Japanese. Only report when kana is present,
            // because a Han-only Japanese track tagged as another language would otherwise
            // be misreported as Chinese.
            var kanaLetters = profile.LettersByScript[SubtitleScriptFamily.Kana];
            var kanaShare = (double)kanaLetters / profile.TotalLetters;
            if (kanaLetters >= 2 && kanaShare >= 0.03)
            {
                return new SubtitleLanguageScriptResult(
                    SubtitleLanguageVerdict.Contradiction,
                    "ja",
                    0.97,
                    "script-ratio+kana",
                    $"Declared '{declared}' expects {LanguageScripts[declared]} but the sample is {dominant} with " +
                    $"{kanaShare:P0} kana; resolved 'ja'.");
            }

            return null;
        }

        if (dominant == SubtitleScriptFamily.Cyrillic)
        {
            var scores = ScoreProfiles(cueSamples, CyrillicProfiles);
            var best = scores
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .FirstOrDefault();
            var language = best.Key != null && best.Value >= MinimumCrossScriptStopwordScore ? best.Key : "ru";
            var method = best.Key != null && best.Value >= MinimumCrossScriptStopwordScore
                ? "script-ratio+stopwords"
                : "script-ratio-cyrillic-fallback";
            return new SubtitleLanguageScriptResult(
                SubtitleLanguageVerdict.Contradiction,
                language,
                0.92,
                method,
                $"Declared '{declared}' expects {LanguageScripts[declared]} but " +
                $"{(double)profile.LettersByScript[SubtitleScriptFamily.Cyrillic] / profile.TotalLetters:P0} of letters are Cyrillic; " +
                $"resolved '{language}'.");
        }

        if (UniqueScriptLanguages.TryGetValue(dominant, out var uniqueLanguage))
        {
            return new SubtitleLanguageScriptResult(
                SubtitleLanguageVerdict.Contradiction,
                uniqueLanguage,
                0.97,
                "script-ratio",
                $"Declared '{declared}' expects {LanguageScripts[declared]} but " +
                $"{(double)profile.LettersByScript[dominant] / profile.TotalLetters:P0} of letters are {dominant}; " +
                $"resolved '{uniqueLanguage}'.");
        }

        return null;
    }

    /// <summary>
    /// Returns true when the declared language is modeled by the contradiction detector.
    /// </summary>
    public static bool IsSupportedDeclaredLanguage(string? language)
    {
        return ResolveLanguageCode(language) != null;
    }

    private static Dictionary<string, double> ScoreProfiles(
        IReadOnlyList<string> cueSamples,
        IReadOnlyList<LanguageProfile> profiles)
    {
        var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var tokensByCue = cueSamples
            .Select(Tokenize)
            .Where(tokens => tokens.Count > 0)
            .ToList();
        if (tokensByCue.Count == 0)
        {
            return scores;
        }

        foreach (var profile in profiles)
        {
            var cuesWithHit = 0;
            var totalHits = 0;
            var totalTokens = 0;
            foreach (var tokens in tokensByCue)
            {
                totalTokens += tokens.Count;
                var hits = tokens.Count(token => profile.FunctionWords.Contains(token));
                if (hits > 0)
                {
                    cuesWithHit++;
                    totalHits += hits;
                }
            }

            if (totalHits == 0)
            {
                continue;
            }

            var cueCoverage = (double)cuesWithHit / tokensByCue.Count;
            var hitDensity = Math.Min(1.0, (double)totalHits / Math.Min(totalTokens, 40));
            scores[profile.Code] = Math.Min(0.99, (cueCoverage * 0.6) + (hitDensity * 0.4));
        }

        return scores;
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var builder = new StringBuilder();
        foreach (var character in text)
        {
            if (char.IsLetter(character) || character == '\'')
            {
                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            if (builder.Length > 0)
            {
                tokens.Add(builder.ToString().Trim('\''));
                builder.Clear();
            }
        }

        if (builder.Length > 0)
        {
            tokens.Add(builder.ToString().Trim('\''));
        }

        return tokens.Where(token => token.Length > 0).ToList();
    }

    private static int CountLetters(string text)
    {
        var count = 0;
        foreach (var character in text)
        {
            if (char.IsLetter(character))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Total number of letters in the supplied cue texts.
    /// </summary>
    public static int CountReadableLetters(IEnumerable<string> cueSamples)
    {
        return cueSamples.Sum(CountLetters);
    }

    private static ScriptProfile CountScripts(IReadOnlyList<string> cueSamples)
    {
        var lettersByScript = Enum.GetValues<SubtitleScriptFamily>()
            .ToDictionary(family => family, _ => 0);
        var totalLetters = 0;

        foreach (var cue in cueSamples)
        {
            foreach (var character in cue)
            {
                if (!char.IsLetter(character))
                {
                    continue;
                }

                totalLetters++;
                lettersByScript[Classify(character)]++;
            }
        }

        var groupedLetters = new Dictionary<SubtitleScriptFamily, int>(lettersByScript);
        groupedLetters[SubtitleScriptFamily.Han] = lettersByScript[SubtitleScriptFamily.Han] + lettersByScript[SubtitleScriptFamily.Kana];

        var dominant = groupedLetters
            .Where(pair => pair.Key != SubtitleScriptFamily.Unknown)
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Select(pair => pair.Key)
            .FirstOrDefault(SubtitleScriptFamily.Unknown);

        return new ScriptProfile(totalLetters, lettersByScript, groupedLetters, dominant);
    }

    /// <summary>
    /// Classifies a letter into a script family by Unicode range. Ranges cover the scripts
    /// the pipeline can realistically receive from media containers and OCR output.
    /// </summary>
    public static SubtitleScriptFamily Classify(char character)
    {
        if (!char.IsLetter(character))
        {
            return SubtitleScriptFamily.Unknown;
        }

        var code = (int)character;

        if ((code >= 0x0041 && code <= 0x005A) ||
            (code >= 0x0061 && code <= 0x007A) ||
            (code >= 0x00C0 && code <= 0x024F) ||
            (code >= 0x1E00 && code <= 0x1EFF))
        {
            return SubtitleScriptFamily.Latin;
        }

        if ((code >= 0x0400 && code <= 0x04FF) ||
            (code >= 0x0500 && code <= 0x052F))
        {
            return SubtitleScriptFamily.Cyrillic;
        }

        if ((code >= 0x0370 && code <= 0x03FF) ||
            (code >= 0x1F00 && code <= 0x1FFF))
        {
            return SubtitleScriptFamily.Greek;
        }

        if ((code >= 0x0600 && code <= 0x06FF) ||
            (code >= 0x0750 && code <= 0x077F))
        {
            return SubtitleScriptFamily.Arabic;
        }

        if (code >= 0x0590 && code <= 0x05FF)
        {
            return SubtitleScriptFamily.Hebrew;
        }

        if (code >= 0x0900 && code <= 0x097F)
        {
            return SubtitleScriptFamily.Devanagari;
        }

        if (code >= 0x0E00 && code <= 0x0E7F)
        {
            return SubtitleScriptFamily.Thai;
        }

        if ((code >= 0x3040 && code <= 0x30FF) ||
            (code >= 0x31F0 && code <= 0x31FF) ||
            (code >= 0xFF66 && code <= 0xFF9D))
        {
            return SubtitleScriptFamily.Kana;
        }

        if ((code >= 0x1100 && code <= 0x11FF) ||
            (code >= 0x3130 && code <= 0x318F) ||
            (code >= 0xA960 && code <= 0xA97F) ||
            (code >= 0xAC00 && code <= 0xD7AF) ||
            (code >= 0xFFA0 && code <= 0xFFDC))
        {
            return SubtitleScriptFamily.Hangul;
        }

        if ((code >= 0x4E00 && code <= 0x9FFF) ||
            (code >= 0x3400 && code <= 0x4DBF) ||
            (code >= 0xF900 && code <= 0xFAFF) ||
            (code >= 0x20000 && code <= 0x2FA1F) ||
            code == 0x3005 || code == 0x3006)
        {
            return SubtitleScriptFamily.Han;
        }

        if (code >= 0x0530 && code <= 0x058F)
        {
            return SubtitleScriptFamily.Armenian;
        }

        if (code >= 0x10A0 && code <= 0x10FF)
        {
            return SubtitleScriptFamily.Georgian;
        }

        // Any other letter stays Unknown: it is counted as a letter but never as evidence
        // for one of the scripts the pipeline models.
        return SubtitleScriptFamily.Unknown;
    }

    private sealed record ScriptProfile(
        int TotalLetters,
        IReadOnlyDictionary<SubtitleScriptFamily, int> LettersByScript,
        IReadOnlyDictionary<SubtitleScriptFamily, int> GroupedLettersByScript,
        SubtitleScriptFamily DominantScript);

    private static LanguageProfile[] BuildProfiles(
        SubtitleScriptFamily script,
        params (string Code, string Words)[] entries)
    {
        return entries
            .Select(entry => new LanguageProfile(
                entry.Code,
                script,
                new HashSet<string>(
                    entry.Words.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Select(word => word.ToLowerInvariant()),
                    StringComparer.OrdinalIgnoreCase)))
            .ToArray();
    }
}
