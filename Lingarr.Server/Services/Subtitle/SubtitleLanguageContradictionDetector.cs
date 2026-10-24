using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.FileSystem;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.Subtitle;

/// <summary>
/// Detects embedded subtitle streams whose content language contradicts the container tag.
/// The check is deterministic first (Unicode script ratio plus function word evidence over
/// cues sampled at 25/50/75 percent of the track) and only falls back to the configured
/// translation provider for Latin-script samples the deterministic pass cannot separate.
/// </summary>
public class SubtitleLanguageContradictionDetector : ISubtitleLanguageContradictionDetector
{
    /// <summary>
    /// Version of the detection algorithm. Bump when heuristics change so cached verdicts
    /// on subtitle rows are re-evaluated.
    /// </summary>
    public const int DetectionVersion = 1;

    private const int CurrentDetectionVersion = DetectionVersion;

    /// <summary>Confidence floor required before a detected language replaces the declared one.</summary>
    public const double MinimumAdoptedConfidence = 0.90;

    /// <summary>Cues requested from the three sample points (2 per point, 6 total).</summary>
    internal const int MaximumSampleCues = 6;

    private const int MinimumMediaDurationSeconds = 90;
    private const int MinimumLettersWithoutDuration = 120;
    private const int MaximumLlmCandidatesPerBatch = 8;
    private const int ExtractionTimeoutSeconds = 30;
    private const int MaximumEvidenceLength = 400;

    internal static readonly TimeSpan PositiveVerdictCache = TimeSpan.FromDays(7);
    internal static readonly TimeSpan NegativeVerdictCache = TimeSpan.FromHours(6);
    private static readonly double[] SamplePositions = [0.25, 0.5, 0.75];

    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settingService;
    private readonly ITranslationServiceFactory? _translationServiceFactory;
    private readonly ISubtitleService _subtitleService;
    private readonly IMediaDurationProvider _durationProvider;
    private readonly ILogger<SubtitleLanguageContradictionDetector> _logger;

    public SubtitleLanguageContradictionDetector(
        LingarrDbContext dbContext,
        ISettingService settingService,
        ISubtitleService subtitleService,
        IMediaDurationProvider durationProvider,
        ILogger<SubtitleLanguageContradictionDetector> logger,
        ITranslationServiceFactory? translationServiceFactory = null)
    {
        _dbContext = dbContext;
        _settingService = settingService;
        _subtitleService = subtitleService;
        _durationProvider = durationProvider;
        _logger = logger;
        _translationServiceFactory = translationServiceFactory;
    }

    /// <inheritdoc />
    public int CurrentVersion => CurrentDetectionVersion;

    /// <inheritdoc />
    public bool IsCheckFresh(EmbeddedSubtitle subtitle, DateTime? now = null)
    {
        if (subtitle.LanguageCheckedAt == null ||
            subtitle.LanguageDetectionVersion != CurrentDetectionVersion)
        {
            return false;
        }

        var cacheWindow = IsNegativeMethod(subtitle.LanguageDetectionMethod)
            ? NegativeVerdictCache
            : PositiveVerdictCache;
        return now.GetValueOrDefault(DateTime.UtcNow) - subtitle.LanguageCheckedAt.Value < cacheWindow;
    }

    /// <inheritdoc />
    public async Task<SubtitleLanguageCheckOutcome> CheckAsync(
        SubtitleLanguageCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        var outcomes = await CheckBatchAsync([request], cancellationToken);
        return outcomes.Count > 0
            ? outcomes[0]
            : CreateInsufficientOutcome(request.Subtitle, "check-skipped", "Language check did not run.");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SubtitleLanguageCheckOutcome>> CheckBatchAsync(
        IReadOnlyList<SubtitleLanguageCheckRequest> requests,
        CancellationToken cancellationToken = default)
    {
        if (requests.Count == 0)
        {
            return [];
        }

        var now = DateTime.UtcNow;
        var outcomes = new List<SubtitleLanguageCheckOutcome>(requests.Count);
        var needsLlm = new List<(int Index, SubtitleLanguageCheckRequest Request, IReadOnlyList<string> Cues)>();

        foreach (var (request, index) in requests.Select((item, i) => (item, i)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!request.ForceRefresh && IsCheckFresh(request.Subtitle, now))
            {
                outcomes.Add(CreateStoredOutcome(request.Subtitle, persisted: false));
                continue;
            }

            var sample = await CollectCueSamplesAsync(
                request.Subtitle,
                request.MediaFilePath,
                cancellationToken);

            if (sample.Verdict != null)
            {
                outcomes.Add(PersistOutcome(request.Subtitle, sample.Verdict, now));
                continue;
            }

            var result = SubtitleScriptAnalyzer.Detect(
                request.Subtitle.Language,
                sample.Cues);

            if (result.Verdict == SubtitleLanguageVerdict.AmbiguousScriptMatch &&
                request.AllowLlmRefinement &&
                needsLlm.Count < MaximumLlmCandidatesPerBatch)
            {
                needsLlm.Add((index, request, sample.Cues));
                outcomes.Add(CreatePendingLlmOutcome(request.Subtitle, result));
                continue;
            }

            outcomes.Add(PersistOutcome(request.Subtitle, result, now));
        }

        if (needsLlm.Count > 0)
        {
            var refined = await TryRefineWithLlmAsync(needsLlm, cancellationToken);
            foreach (var (index, llmResult) in refined)
            {
                var request = requests[index];
                if (llmResult == null)
                {
                    var stored = CreateStoredOutcome(request.Subtitle, persisted: false);
                    outcomes[index] = PersistOutcome(
                        request.Subtitle,
                        new SubtitleLanguageScriptResult(
                            SubtitleLanguageVerdict.AmbiguousScriptMatch,
                            null,
                            0,
                            "ambiguous-latin",
                            stored.Evidence),
                        now);
                    continue;
                }

                outcomes[index] = PersistOutcome(request.Subtitle, llmResult, now);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return outcomes;
    }

    /// <inheritdoc />
    public async Task<EmbeddedSubtitle?> TryAdoptDetectedSourceLanguageAsync(
        TranslationRequest request,
        IReadOnlyList<EmbeddedSubtitle> embeddedSubtitles,
        string mediaFilePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SourceLanguage))
        {
            return null;
        }

        var candidates = embeddedSubtitles
            .Where(subtitle => subtitle.IsReadableSource())
            .Where(subtitle => SubtitleScriptAnalyzer.IsSupportedDeclaredLanguage(subtitle.Language))
            .Take(4)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var checkRequests = candidates
            .Select(subtitle => new SubtitleLanguageCheckRequest(
                subtitle,
                mediaFilePath,
                AllowLlmRefinement: true,
                ForceRefresh: false))
            .ToList();

        var outcomes = await CheckBatchAsync(checkRequests, cancellationToken);

        // First prefer a stream whose detected language already matches the requested source
        // language: the request needs no language rewrite, only the stream pick changes.
        var effectiveMatch = SelectEffectiveLanguageMatch(outcomes, request.SourceLanguage);
        if (effectiveMatch != null)
        {
            _logger.LogInformation(
                "Adopting stream {StreamIndex} with detected language {DetectedLanguage} for request {RequestId} " +
                "(requested {SourceLanguage}, confidence {Confidence:F2})",
                effectiveMatch.Subtitle.StreamIndex,
                SubtitleLanguageHelper.GetEffectiveLanguage(effectiveMatch.Subtitle),
                request.Id,
                request.SourceLanguage,
                effectiveMatch.Confidence);
            return effectiveMatch.Subtitle;
        }

        // Otherwise adopt when the request's declared source language is the tag that
        // contradicted its own content (for example a jpn tag containing English).
        var adopted = SelectContradictedDeclaredTag(outcomes, request.SourceLanguage);

        if (adopted == null)
        {
            return null;
        }

        var detectedLanguage = SubtitleLanguageHelper.GetEffectiveLanguage(adopted.Subtitle);
        if (detectedLanguage == null)
        {
            return null;
        }

        _logger.LogInformation(
            "Adopting detected source language {DetectedLanguage} for request {RequestId} (declared {DeclaredLanguage}, confidence {Confidence:F2}, method {Method})",
            detectedLanguage,
            request.Id,
            request.SourceLanguage,
            adopted.Confidence,
            adopted.Method);

        request.SourceLanguage = detectedLanguage;
        request.SourceSubtitleFormat = adopted.Subtitle.GetReadableSourceFormat();

        await _dbContext.SaveChangesAsync(cancellationToken);
        return adopted.Subtitle;
    }

    /// <summary>
    /// Picks the highest confidence stream whose effective language already matches the
    /// request, so the resolver can switch streams without rewriting the request language.
    /// Verdicts below <see cref="MinimumAdoptedConfidence"/> are never adopted.
    /// </summary>
    internal static SubtitleLanguageCheckOutcome? SelectEffectiveLanguageMatch(
        IEnumerable<SubtitleLanguageCheckOutcome> outcomes,
        string sourceLanguage)
    {
        return outcomes
            .Where(outcome => outcome.Confidence >= MinimumAdoptedConfidence)
            .Where(outcome => SubtitleLanguageHelper.LanguageMatchesEffective(outcome.Subtitle, sourceLanguage))
            .OrderByDescending(outcome => outcome.Confidence)
            .ThenBy(outcome => outcome.Subtitle.StreamIndex)
            .FirstOrDefault();
    }

    /// <summary>
    /// Picks the highest confidence stream whose declared tag matches the request but whose
    /// effective language contradicts it, i.e. the stream whose content should be adopted.
    /// Verdicts below <see cref="MinimumAdoptedConfidence"/> are never adopted.
    /// </summary>
    internal static SubtitleLanguageCheckOutcome? SelectContradictedDeclaredTag(
        IEnumerable<SubtitleLanguageCheckOutcome> outcomes,
        string sourceLanguage)
    {
        return outcomes
            .Where(outcome => outcome.Confidence >= MinimumAdoptedConfidence)
            .Where(outcome => SubtitleLanguageHelper.LanguageMatches(outcome.Subtitle.Language, sourceLanguage))
            .Where(outcome => !SubtitleLanguageHelper.LanguageMatchesEffective(outcome.Subtitle, sourceLanguage))
            .OrderByDescending(outcome => outcome.Confidence)
            .ThenBy(outcome => outcome.Subtitle.StreamIndex)
            .FirstOrDefault();
    }

    private async Task<EmbeddedSubtitleCheckSample> CollectCueSamplesAsync(
        EmbeddedSubtitle subtitle,
        string mediaFilePath,
        CancellationToken cancellationToken)
    {
        var duration = await _durationProvider.GetDurationSecondsAsync(mediaFilePath, cancellationToken);
        if (duration.HasValue && duration.Value < MinimumMediaDurationSeconds)
        {
            return EmbeddedSubtitleCheckSample.Failed(new SubtitleLanguageScriptResult(
                SubtitleLanguageVerdict.Insufficient,
                null,
                0,
                "media-too-short",
                $"Media duration {duration.Value:F0}s is below the {MinimumMediaDurationSeconds}s guard; not judging language."));
        }

        if (!subtitle.IsReadableSource())
        {
            return EmbeddedSubtitleCheckSample.Failed(new SubtitleLanguageScriptResult(
                SubtitleLanguageVerdict.Insufficient,
                null,
                0,
                "no-readable-stream",
                "Stream is bitmap based without usable OCR output; language cannot be read."));
        }

        List<string> cues;
        var readablePath = subtitle.GetReadableSourcePath();
        if (!string.IsNullOrWhiteSpace(readablePath) && File.Exists(readablePath))
        {
            cues = await ReadCueTextsAsync(readablePath, cancellationToken);
        }
        else if (subtitle.IsTextBased && File.Exists(mediaFilePath))
        {
            cues = await ExtractCueTextsAsync(subtitle, mediaFilePath, cancellationToken);
        }
        else
        {
            return EmbeddedSubtitleCheckSample.Failed(new SubtitleLanguageScriptResult(
                SubtitleLanguageVerdict.Insufficient,
                null,
                0,
                "extraction-failed",
                "No readable subtitle file and the stream could not be extracted for language analysis."));
        }

        if (cues.Count == 0)
        {
            return EmbeddedSubtitleCheckSample.Failed(new SubtitleLanguageScriptResult(
                SubtitleLanguageVerdict.Insufficient,
                null,
                0,
                "no-cues",
                "The subtitle stream produced no readable cues."));
        }

        var sampled = SampleCuesAtPositions(cues);
        var letters = SubtitleScriptAnalyzer.CountReadableLetters(sampled);
        if (!duration.HasValue && letters < MinimumLettersWithoutDuration)
        {
            return EmbeddedSubtitleCheckSample.Failed(new SubtitleLanguageScriptResult(
                SubtitleLanguageVerdict.Insufficient,
                null,
                0,
                "insufficient-sample",
                $"Media duration is unknown and the sample has only {letters} letters; at least " +
                $"{MinimumLettersWithoutDuration} are required without a duration guard."));
        }

        return new EmbeddedSubtitleCheckSample(sampled, null);
    }

    /// <summary>
    /// Samples up to six cues from the 25/50/75 percent timeline positions. The cue list is
    /// already ordered, so index percentiles approximate timeline percentiles.
    /// </summary>
    public static IReadOnlyList<string> SampleCuesAtPositions(
        IReadOnlyList<string> cueTexts,
        int maximumCues = MaximumSampleCues)
    {
        var readable = cueTexts
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Where(text => text.Count(char.IsLetter) >= 3)
            .ToList();

        if (readable.Count < 2 || maximumCues <= 0)
        {
            return [];
        }

        var indexes = new List<int>(maximumCues);
        foreach (var position in SamplePositions)
        {
            if (indexes.Count >= maximumCues)
            {
                break;
            }

            var center = (int)Math.Round((readable.Count - 1) * position, MidpointRounding.AwayFromZero);
            center = Math.Clamp(center, 0, readable.Count - 1);

            var first = Math.Max(0, Math.Min(center, readable.Count - 1));
            AddIndex(indexes, first, maximumCues);

            if (indexes.Count >= maximumCues)
            {
                break;
            }

            var second = first + 1 < readable.Count ? first + 1 : first - 1;
            if (second >= 0 && second < readable.Count)
            {
                AddIndex(indexes, second, maximumCues);
            }
        }

        indexes.Sort();
        return indexes.Select(index => readable[index]).ToList();
    }

    private static void AddIndex(List<int> indexes, int index, int maximumCues)
    {
        if (indexes.Count >= maximumCues || indexes.Contains(index))
        {
            return;
        }

        indexes.Add(index);
    }

    private async Task<List<string>> ReadCueTextsAsync(string subtitlePath, CancellationToken cancellationToken)
    {
        try
        {
            var items = await _subtitleService.ReadSubtitles(subtitlePath);
            cancellationToken.ThrowIfCancellationRequested();
            return items.Select(GetCueText).Where(text => text.Length > 0).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read subtitle cues from {Path} for language detection", subtitlePath);
            return [];
        }
    }

    private async Task<List<string>> ExtractCueTextsAsync(
        EmbeddedSubtitle subtitle,
        string mediaFilePath,
        CancellationToken cancellationToken)
    {
        var normalizedFormat = SubtitleOutputModeHelper.NormalizeFormat(subtitle.CodecName);
        var extension = SubtitleOutputModeHelper.IsAssFormat(subtitle.CodecName)
            ? ".ass"
            : normalizedFormat is ".vtt" or ".webvtt" ? ".vtt" : ".srt";
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"lingarr-langcheck-{Guid.NewGuid():N}{extension}");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-v");
            startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(mediaFilePath);
            startInfo.ArgumentList.Add("-map");
            startInfo.ArgumentList.Add($"0:s:{subtitle.StreamIndex}");
            startInfo.ArgumentList.Add("-c:s");
            startInfo.ArgumentList.Add("copy");
            startInfo.ArgumentList.Add(tempPath);

            using var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not start ffmpeg for language detection on {Path}", mediaFilePath);
                return [];
            }

            var errorTask = process.StandardError.ReadToEndAsync();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(ExtractionTimeoutSeconds));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                await IgnoreFaultAsync(errorTask);
                return [];
            }

            await IgnoreFaultAsync(errorTask);
            if (process.ExitCode != 0 || !File.Exists(tempPath))
            {
                return [];
            }

            return await ReadCueTextsAsync(tempPath, cancellationToken);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private async Task<Dictionary<int, SubtitleLanguageScriptResult?>> TryRefineWithLlmAsync(
        IReadOnlyList<(int Index, SubtitleLanguageCheckRequest Request, IReadOnlyList<string> Cues)> candidates,
        CancellationToken cancellationToken)
    {
        var results = candidates.ToDictionary(candidate => candidate.Index, _ => (SubtitleLanguageScriptResult?)null);

        if (_translationServiceFactory == null)
        {
            return results;
        }

        var serviceType = await _settingService.GetSetting(SettingKeys.Translation.ServiceType);
        if (string.IsNullOrWhiteSpace(serviceType))
        {
            return results;
        }

        ITranslationService translationService;
        try
        {
            translationService = _translationServiceFactory.CreateTranslationService(serviceType);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cannot create translation service for batched language detection");
            return results;
        }

        var prompt = BuildLatinRefinementPrompt(candidates);
        string response;
        try
        {
            response = await translationService.TranslateAsync(prompt, "en", "en", null, null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Batched language detection call failed; keeping deterministic verdicts");
            return results;
        }

        var detected = ParseBatchResponse(response);
        foreach (var candidate in candidates)
        {
            if (!detected.TryGetValue(candidate.Index, out var code) ||
                string.IsNullOrWhiteSpace(code) ||
                !SubtitleLanguageHelper.TryNormalizeKnownLanguageCode(code, out var normalized) ||
                !SubtitleScriptAnalyzer.IsSupportedDeclaredLanguage(normalized) ||
                SubtitleLanguageHelper.LanguageMatches(normalized, candidate.Request.Subtitle.Language))
            {
                continue;
            }

            results[candidate.Index] = new SubtitleLanguageScriptResult(
                SubtitleLanguageVerdict.Contradiction,
                normalized,
                0.90,
                "llm-batch",
                $"Deterministic script analysis found Latin text contradicting declared " +
                $"'{candidate.Request.Subtitle.Language}'; batched provider detection resolved '{normalized}'.");
        }

        return results;
    }

    private static string BuildLatinRefinementPrompt(
        IReadOnlyList<(int Index, SubtitleLanguageCheckRequest Request, IReadOnlyList<string> Cues)> candidates)
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            "Identify the language of each subtitle sample. Respond with ONLY a JSON array of " +
            "objects with \"sample\" (integer) and \"language\" (lowercase ISO 639-1 code such as " +
            "\"en\", \"es\", \"fr\", \"de\", \"pt\", \"it\", \"nl\", \"pl\", \"ro\", \"tr\", \"id\", \"vi\"). " +
            "Use \"und\" when the sample is not clearly one language. No markdown, no explanation.");
        builder.AppendLine();

        foreach (var candidate in candidates)
        {
            builder.AppendLine($"Sample {candidate.Index}:");
            foreach (var cue in candidate.Cues.Take(6))
            {
                builder.AppendLine(cue.Trim());
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static Dictionary<int, string> ParseBatchResponse(string response)
    {
        var result = new Dictionary<int, string>();
        if (string.IsNullOrWhiteSpace(response))
        {
            return result;
        }

        var json = response.Trim();
        var fenceStart = json.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var afterFence = json.IndexOf('\n', fenceStart);
            var fenceEnd = json.LastIndexOf("```", StringComparison.Ordinal);
            if (afterFence >= 0 && fenceEnd > afterFence)
            {
                json = json.Substring(afterFence + 1, fenceEnd - afterFence - 1).Trim();
            }
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (!element.TryGetProperty("sample", out var sampleProperty) ||
                    !element.TryGetProperty("language", out var languageProperty) ||
                    !sampleProperty.TryGetInt32(out var sample) ||
                    languageProperty.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                result[sample] = languageProperty.GetString() ?? "und";
            }
        }
        catch (JsonException)
        {
            return result;
        }

        return result;
    }

    private SubtitleLanguageCheckOutcome PersistOutcome(
        EmbeddedSubtitle subtitle,
        SubtitleLanguageScriptResult result,
        DateTime checkedAt)
    {
        var detectedLanguage = result.Verdict == SubtitleLanguageVerdict.Contradiction
            ? SubtitleLanguageHelper.NormalizeLanguageCode(result.DetectedLanguage)
            : null;

        subtitle.DetectedLanguage = string.IsNullOrWhiteSpace(detectedLanguage) ? null : detectedLanguage;
        subtitle.DetectedLanguageConfidence = subtitle.DetectedLanguage == null
            ? null
            : Math.Clamp(result.Confidence, 0, 1);
        subtitle.LanguageDetectionMethod = result.Method;
        subtitle.LanguageDetectionEvidence = TruncateEvidence(result.Evidence);
        subtitle.LanguageCheckedAt = checkedAt;
        subtitle.LanguageDetectionVersion = CurrentDetectionVersion;

        _logger.LogInformation(
            "Subtitle language check: stream {StreamIndex} declared '{Declared}', verdict {Verdict}, detected '{Detected}' " +
            "(confidence {Confidence:F2}, method {Method})",
            subtitle.StreamIndex,
            subtitle.Language ?? "unknown",
            result.Verdict,
            subtitle.DetectedLanguage ?? "none",
            result.Confidence,
            result.Method);


        return new SubtitleLanguageCheckOutcome(
            subtitle,
            result.Verdict,
            subtitle.Language,
            subtitle.DetectedLanguage,
            result.Confidence,
            result.Method,
            subtitle.LanguageDetectionEvidence,
            Persisted: true);
    }

    private static SubtitleLanguageCheckOutcome CreateStoredOutcome(EmbeddedSubtitle subtitle, bool persisted)
    {
        return new SubtitleLanguageCheckOutcome(
            subtitle,
            string.IsNullOrWhiteSpace(subtitle.DetectedLanguage)
                ? SubtitleLanguageVerdict.Consistent
                : SubtitleLanguageVerdict.Contradiction,
            subtitle.Language,
            subtitle.DetectedLanguage,
            subtitle.DetectedLanguageConfidence ?? 0,
            subtitle.LanguageDetectionMethod ?? "cached",
            subtitle.LanguageDetectionEvidence ?? "Cached language check result.",
            persisted);
    }

    private static SubtitleLanguageCheckOutcome CreatePendingLlmOutcome(
        EmbeddedSubtitle subtitle,
        SubtitleLanguageScriptResult result)
    {
        return new SubtitleLanguageCheckOutcome(
            subtitle,
            SubtitleLanguageVerdict.AmbiguousScriptMatch,
            subtitle.Language,
            null,
            0,
            "ambiguous-latin",
            result.Evidence,
            Persisted: false);
    }

    private static SubtitleLanguageCheckOutcome CreateInsufficientOutcome(
        EmbeddedSubtitle subtitle,
        string method,
        string evidence)
    {
        return new SubtitleLanguageCheckOutcome(
            subtitle,
            SubtitleLanguageVerdict.Insufficient,
            subtitle.Language,
            null,
            0,
            method,
            evidence,
            Persisted: false);
    }

    private static bool IsNegativeMethod(string? method)
    {
        return string.IsNullOrWhiteSpace(method) ||
               method is "insufficient-sample" or "media-too-short" or "no-readable-stream" or
                   "extraction-failed" or "no-cues" or "ambiguous-latin" or "transient-error";
    }

    private static string GetCueText(SubtitleItem item)
    {
        var lines = item.PlaintextLines.Count > 0 ? item.PlaintextLines : item.Lines;
        return string.Join(' ', lines).Trim();
    }

    private static string TruncateEvidence(string evidence)
    {
        return evidence.Length <= MaximumEvidenceLength
            ? evidence
            : evidence[..MaximumEvidenceLength];
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _ = ex;
        }
    }

    private static async Task IgnoreFaultAsync(Task<string> readTask)
    {
        try
        {
            await readTask;
        }
        catch
        {
            // The process was killed or its stderr stream closed; nothing to surface here.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temp cleanup failures are not actionable for language detection.
        }
    }

    private sealed record EmbeddedSubtitleCheckSample(
        IReadOnlyList<string> Cues,
        SubtitleLanguageScriptResult? Verdict)
    {
        public static EmbeddedSubtitleCheckSample Failed(SubtitleLanguageScriptResult verdict)
        {
            return new EmbeddedSubtitleCheckSample([], verdict);
        }
    }
}
