using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Services.Subtitle;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public sealed class SubtitleLanguageContradictionTests
{
    // Real cues from the live Beyblade S01E28 embedded ASS track whose container tag is jpn
    // while the content is English (episode 171444, cached as episode-171444-stream-1-jpn).
    private static readonly string[] BeybladeEnglishCues =
    [
        "Battle Bladers.",
        "That is the greatest stage, where the number one Blader will be decided.",
        "Behind the dreams of would-be participants, a dark conspiracy swirls ominously!",
        "This is the story of passionate Bladers who, in order to hinder these evil aspirations, are aiming for Battle Bladers!",
        "Everyone is racking in the points.",
        "I promised I would win so Taka-chan could get better."
    ];

    // Real Japanese fixture lines from Lingarr.Server.Tests/Services/SubtitleTranslationServiceTests.cs.
    private static readonly string[] JapaneseCues =
    [
        "静流は大好きなゲームを迷わず諦める",
        "ただ俺の負担にならないように",
        "それに彼女は生活を維持しようと必死で",
        "でも俺にも静流にしてやれることはある",
        "静かにしろよ、拉致するからな"
    ];

    // Real simplified Chinese fixture lines from SubtitleQualityValidatorServiceTests.cs.
    private static readonly string[] ChineseCues =
    [
        "赶紧把路修好然后离开这里",
        "你能不能关掉那不尊重人的垃圾",
        "尊重经典吧我的朋友",
        "他一定是在我们都睡着的时候修好的",
        "我们可不想他错过那场比赛"
    ];

    private static readonly string[] RussianCues =
    [
        "Привет, как дела? Я не знаю, что делать.",
        "Мы должны идти домой сейчас, это очень важно для нас.",
        "Ты можешь сказать мне, где находится вокзал?",
        "Она не хочет говорить об этом сегодня вечером."
    ];

    private static readonly string[] UkrainianCues =
    [
        "Привіт, як справи? Я не знаю, що робити сьогодні ввечері.",
        "Ми повинні йти додому зараз, це дуже важливо для нас.",
        "Ти можеш сказати мені, де знаходиться вокзал, будь ласка?",
        "Вона не хоче говорити про це сьогодні ввечері, але я наполягаю.",
        "Після роботи ми підемо до магазину разом з дітьми, якщо буде час."
    ];

    private static readonly string[] BulgarianCues =
    [
        "Здравей, как си? Аз не знам какво да правя тази вечер.",
        "Ние трябва да тръгваме към вкъщи сега, това е много важно за нас.",
        "Можеш ли да ми кажеш къде се намира гарата, моля те?",
        "Тя не иска да говори за това тази вечер, но аз настоявам.",
        "След работа ще отидем до магазина заедно с децата, ако имаме време."
    ];

    private static readonly string[] SpanishCues =
    [
        "El hombre que estaba de pie dijo que no vendría al cine esta noche.",
        "Queremos el libro de la mesa y el mapa que estaba en la pared de la sala.",
        "Ella dijo que su madre estaba en casa todo el día y que no quería salir.",
        "Por eso no queremos ir con ellos esta noche porque tenemos mucho trabajo.",
        "La verdad es que no tenemos mucho tiempo para esto pero lo vamos a intentar.",
        "Cuando el sol se pone, todos los niños vuelven a casa con sus padres."
    ];

    private static readonly string[] KoreanCues =
    [
        "안녕하세요, 오늘 날씨가 정말 좋네요.",
        "우리는 지금 영화를 보고 있어요.",
        "내일 다시 만나서 이야기해요.",
        "그 사람은 우리에게 아무 말도 하지 않았어요.",
        "이 문제는 생각보다 훨씬 어려워요."
    ];

    [Fact]
    public void Detect_JapaneseTagContainingEnglishContent_ReportsEnglishContradiction()
    {
        var result = SubtitleScriptAnalyzer.Detect("jpn", BeybladeEnglishCues);

        Assert.Equal(SubtitleLanguageVerdict.Contradiction, result.Verdict);
        Assert.Equal("en", result.DetectedLanguage);
        Assert.True(result.Confidence >= 0.90);
        Assert.Contains("Latin", result.Evidence);
    }

    [Fact]
    public void Detect_CorrectlyTaggedEnglishContent_DoesNotOverride()
    {
        var result = SubtitleScriptAnalyzer.Detect("en", BeybladeEnglishCues);

        Assert.Equal(SubtitleLanguageVerdict.Consistent, result.Verdict);
        Assert.Null(result.DetectedLanguage);
    }

    [Fact]
    public void Detect_CorrectlyTaggedJapaneseContent_DoesNotOverride()
    {
        // Regression guard for the old Latin-vowel heuristic that treated CJK as corrupt.
        var result = SubtitleScriptAnalyzer.Detect("ja", JapaneseCues);

        Assert.Equal(SubtitleLanguageVerdict.Consistent, result.Verdict);
        Assert.Null(result.DetectedLanguage);
    }

    [Fact]
    public void Detect_CorrectlyTaggedChineseContent_DoesNotOverride()
    {
        var result = SubtitleScriptAnalyzer.Detect("zh", ChineseCues);

        Assert.Equal(SubtitleLanguageVerdict.Consistent, result.Verdict);
        Assert.Null(result.DetectedLanguage);
    }

    [Fact]
    public void Detect_LatinTagContainingJapaneseKana_ReportsJapanese()
    {
        var result = SubtitleScriptAnalyzer.Detect("en", JapaneseCues);

        Assert.Equal(SubtitleLanguageVerdict.Contradiction, result.Verdict);
        Assert.Equal("ja", result.DetectedLanguage);
        Assert.True(result.Confidence >= 0.90);
    }

    [Fact]
    public void Detect_EnglishTagContainingRussian_ReportsRussianFromCyrillicStopwords()
    {
        var result = SubtitleScriptAnalyzer.Detect("en", RussianCues);

        Assert.Equal(SubtitleLanguageVerdict.Contradiction, result.Verdict);
        Assert.Equal("ru", result.DetectedLanguage);
        Assert.True(result.Confidence >= 0.90);
        // The word lists must actually match; the old corrupted lists silently fell back to ru.
        Assert.Equal("script-ratio+stopwords", result.Method);
    }

    [Fact]
    public void Detect_EnglishTagContainingUkrainian_ReportsUkrainian()
    {
        var result = SubtitleScriptAnalyzer.Detect("en", UkrainianCues);

        Assert.Equal(SubtitleLanguageVerdict.Contradiction, result.Verdict);
        Assert.Equal("uk", result.DetectedLanguage);
        Assert.Equal("script-ratio+stopwords", result.Method);
        Assert.True(result.Confidence >= 0.90);
    }

    [Fact]
    public void Detect_EnglishTagContainingBulgarian_ReportsBulgarian()
    {
        var result = SubtitleScriptAnalyzer.Detect("en", BulgarianCues);

        Assert.Equal(SubtitleLanguageVerdict.Contradiction, result.Verdict);
        Assert.Equal("bg", result.DetectedLanguage);
        Assert.Equal("script-ratio+stopwords", result.Method);
        Assert.True(result.Confidence >= 0.90);
    }

    [Fact]
    public void Detect_EnglishTagContainingBulgarian_DoesNotFallBackToRussian()
    {
        // Regression guard for corrupted Cyrillic profiles: every Cyrillic match used to
        // return the hardcoded ru fallback because no function word could ever hit.
        var result = SubtitleScriptAnalyzer.Detect("en", BulgarianCues);

        Assert.Equal("bg", result.DetectedLanguage);
        Assert.DoesNotContain("fallback", result.Method);
    }

    [Fact]
    public void FunctionWordProfiles_EveryWordBelongsToItsDeclaredScript()
    {
        var profiles = SubtitleScriptAnalyzer.FunctionWordProfiles;

        Assert.Equal(18 + 3, profiles.Count);
        foreach (var (code, script, functionWords) in profiles)
        {
            Assert.True(functionWords.Count > 20, $"{code} has too few function words");
            foreach (var word in functionWords)
            {
                Assert.False(string.IsNullOrWhiteSpace(word), $"{code} contains an empty word");
                Assert.All(
                    word,
                    character => Assert.Equal(script, SubtitleScriptAnalyzer.Classify(character)));
            }
        }
    }

    [Fact]
    public void CyrillicProfiles_ContainRealCyrillicWordsAndDistinctiveLetters()
    {
        var profiles = SubtitleScriptAnalyzer.FunctionWordProfiles
            .ToDictionary(profile => profile.Code, profile => profile.FunctionWords);

        Assert.All(
            profiles["ru"],
            word => Assert.All(word, character => Assert.InRange(character, '\u0400', '\u04FF')));

        Assert.Contains(
            profiles["uk"],
            word => word.Any(character => character is '\u0456' or '\u0457' or '\u0454'));
        Assert.Contains(
            profiles["bg"],
            word => word.Any(character => character == '\u044A'));
    }

    [Fact]
    public void Detect_EnglishTagContainingKorean_ReportsKorean()
    {
        var result = SubtitleScriptAnalyzer.Detect("en", KoreanCues);

        Assert.Equal(SubtitleLanguageVerdict.Contradiction, result.Verdict);
        Assert.Equal("ko", result.DetectedLanguage);
        Assert.True(result.Confidence >= 0.90);
    }

    [Fact]
    public void Detect_EnglishTagContainingSpanish_ReportsSpanish()
    {
        var result = SubtitleScriptAnalyzer.Detect("en", SpanishCues);

        Assert.Equal(SubtitleLanguageVerdict.Contradiction, result.Verdict);
        Assert.Equal("es", result.DetectedLanguage);
        Assert.True(result.Confidence >= 0.90);
    }

    [Fact]
    public void Detect_ShortNamesOnly_IsInsufficientInsteadOfGuessing()
    {
        // Names and short quoted words are not enough evidence for any language claim.
        var result = SubtitleScriptAnalyzer.Detect(
            "jpn",
            ["Ginga.", "Kenta, Benkei.", "Kyouya!", "Hyouma?"]);

        Assert.Equal(SubtitleLanguageVerdict.Insufficient, result.Verdict);
        Assert.Null(result.DetectedLanguage);
    }

    [Fact]
    public void Detect_MixedScriptSample_IsNotJudged()
    {
        var cues = new List<string>
        {
            "这是一行中文字幕内容而且很长",
            "This line is a fairly long English sentence with plenty of words in it",
            "Привет, это довольно длинный русский текст для проверки языка",
            "Ceci est une phrase francaise assez longue avec beaucoup de mots dedans"
        };

        var result = SubtitleScriptAnalyzer.Detect("ja", cues);

        Assert.Equal(SubtitleLanguageVerdict.Insufficient, result.Verdict);
        Assert.Null(result.DetectedLanguage);
    }

    [Fact]
    public void SampleCuesAtPositions_Uses25_50_75PercentOfTheTrack()
    {
        var cues = Enumerable.Range(1, 100)
            .Select(index => $"Dialogue line number {index} with enough letters.")
            .ToList();

        var sampled = SubtitleLanguageContradictionDetector.SampleCuesAtPositions(cues);

        Assert.Equal(6, sampled.Count);
        Assert.Equal(6, sampled.Distinct().Count());

        var indexes = sampled
            .Select(cue => int.Parse(cue.Split(' ')[3]))
            .OrderBy(index => index)
            .ToList();

        // Two cues from each of the 25/50/75 percent positions (1-based cue numbers).
        Assert.Contains(indexes, index => index is 25 or 26);
        Assert.Contains(indexes, index => index is 50 or 51);
        Assert.Contains(indexes, index => index is 75 or 76);
    }

    [Fact]
    public void SampleCuesAtPositions_SkipsShortCuesAndRefusesSingleCueSamples()
    {
        Assert.Empty(SubtitleLanguageContradictionDetector.SampleCuesAtPositions(
            ["a", "12", "-", "♪"]));

        // A single readable cue is never enough evidence for a language claim.
        Assert.Empty(SubtitleLanguageContradictionDetector.SampleCuesAtPositions(
            ["This is the only readable cue in the stream."]));
    }

    [Fact]
    public void GetEffectiveLanguage_HighConfidenceDetection_OverridesDeclaredTag()
    {
        var subtitle = new EmbeddedSubtitle
        {
            Language = "jpn",
            DetectedLanguage = "en",
            DetectedLanguageConfidence = 0.96,
            LanguageDetectionVersion = SubtitleLanguageHelper.CurrentLanguageDetectionVersion
        };

        Assert.Equal("en", SubtitleLanguageHelper.GetEffectiveLanguage(subtitle));
        Assert.True(SubtitleLanguageHelper.LanguageMatchesEffective(subtitle, "en"));
        Assert.False(SubtitleLanguageHelper.LanguageMatchesEffective(subtitle, "ja"));
        Assert.Equal("jpn", subtitle.Language);
    }

    [Fact]
    public void GetEffectiveLanguage_LowConfidenceOrStaleVersion_KeepsDeclaredTag()
    {
        var lowConfidence = new EmbeddedSubtitle
        {
            Language = "jpn",
            DetectedLanguage = "en",
            DetectedLanguageConfidence = 0.60,
            LanguageDetectionVersion = SubtitleLanguageHelper.CurrentLanguageDetectionVersion
        };
        Assert.Equal("ja", SubtitleLanguageHelper.GetEffectiveLanguage(lowConfidence));

        var staleVersion = new EmbeddedSubtitle
        {
            Language = "jpn",
            DetectedLanguage = "en",
            DetectedLanguageConfidence = 0.99,
            LanguageDetectionVersion = SubtitleLanguageHelper.CurrentLanguageDetectionVersion - 1
        };
        Assert.Equal("ja", SubtitleLanguageHelper.GetEffectiveLanguage(staleVersion));
    }

    [Fact]
    public async Task SourceSelection_DetectedLanguage_MatchesConfiguredSourceLanguage()
    {
        var subtitle = new EmbeddedSubtitle
        {
            StreamIndex = 0,
            Language = "jpn",
            Title = "Full",
            CodecName = "subrip",
            IsTextBased = true,
            DetectedLanguage = "en",
            DetectedLanguageConfidence = 0.96,
            LanguageDetectionVersion = SubtitleLanguageHelper.CurrentLanguageDetectionVersion
        };
        var selectionService = new SubtitleSourceSelectionService(
            Mock.Of<Lingarr.Server.Interfaces.Services.ISubtitleService>(),
            NullLogger<SubtitleSourceSelectionService>.Instance);

        var result = await selectionService.SelectPrimaryAsync(
            [subtitle],
            ["en"],
            allowCaptionFallback: false);

        Assert.NotNull(result.SelectedSubtitle);
        Assert.Equal("en", result.MatchedLanguage);
    }

    [Fact]
    public void SelectEffectiveLanguageMatch_IgnoresVerdictsBelowAdoptedConfidenceFloor()
    {
        var subtitle = DetectedEnglishSubtitle(confidence: 0.96);
        var outcomes = new[]
        {
            Outcome(subtitle, SubtitleLanguageVerdict.Contradiction, "en", confidence: 0.89, persisted: true),
            Outcome(subtitle, SubtitleLanguageVerdict.Contradiction, "en", confidence: 0.90, persisted: true)
        };

        var belowFloor = SubtitleLanguageContradictionDetector.SelectEffectiveLanguageMatch(
            outcomes.Take(1),
            "en");
        var atFloor = SubtitleLanguageContradictionDetector.SelectEffectiveLanguageMatch(
            outcomes.Skip(1),
            "en");

        Assert.Null(belowFloor);
        Assert.NotNull(atFloor);
        Assert.Equal(0.90, atFloor.Confidence);
    }

    [Fact]
    public void SelectContradictedDeclaredTag_IgnoresVerdictsBelowAdoptedConfidenceFloor()
    {
        var subtitle = DetectedEnglishSubtitle(confidence: 0.96);
        var outcomes = new[]
        {
            Outcome(subtitle, SubtitleLanguageVerdict.Contradiction, "en", confidence: 0.89, persisted: true),
            Outcome(subtitle, SubtitleLanguageVerdict.Contradiction, "en", confidence: 0.90, persisted: true)
        };

        var belowFloor = SubtitleLanguageContradictionDetector.SelectContradictedDeclaredTag(
            outcomes.Take(1),
            "jpn");
        var atFloor = SubtitleLanguageContradictionDetector.SelectContradictedDeclaredTag(
            outcomes.Skip(1),
            "jpn");

        Assert.Null(belowFloor);
        Assert.NotNull(atFloor);
        Assert.Equal(0.90, atFloor.Confidence);
    }

    private static EmbeddedSubtitle DetectedEnglishSubtitle(double confidence)
    {
        return new EmbeddedSubtitle
        {
            StreamIndex = 1,
            Language = "jpn",
            Title = "ASS",
            CodecName = "ass",
            IsTextBased = true,
            IsForced = true,
            DetectedLanguage = "en",
            DetectedLanguageConfidence = confidence,
            LanguageDetectionMethod = "script-ratio+stopwords",
            LanguageDetectionVersion = SubtitleLanguageHelper.CurrentLanguageDetectionVersion
        };
    }

    private static SubtitleLanguageCheckOutcome Outcome(
        EmbeddedSubtitle subtitle,
        SubtitleLanguageVerdict verdict,
        string detectedLanguage,
        double confidence,
        bool persisted)
    {
        return new SubtitleLanguageCheckOutcome(
            subtitle,
            verdict,
            subtitle.Language,
            detectedLanguage,
            confidence,
            "script-ratio+stopwords",
            "test evidence",
            persisted);
    }
}
