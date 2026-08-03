using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;

namespace Lingarr.Server.Services.Subtitle;

public class SubtitleSourceSelectionService : ISubtitleSourceSelectionService
{
    private const int LanguagePriorityBonus = 20;
    private const double MinimumAutoFallbackScore = 50.0;
    private readonly ISubtitleService _subtitleService;
    private readonly ITranslationQualityScorer? _qualityScorer;
    private readonly ILogger<SubtitleSourceSelectionService> _logger;

    public SubtitleSourceSelectionService(
        ISubtitleService subtitleService,
        ILogger<SubtitleSourceSelectionService> logger,
        ITranslationQualityScorer? qualityScorer = null)
    {
        _subtitleService = subtitleService;
        _qualityScorer = qualityScorer;
        _logger = logger;
    }

    public async Task<SubtitleSourceSelectionResult> SelectPrimaryAsync(
        IReadOnlyCollection<EmbeddedSubtitle> candidates,
        IReadOnlyList<string> configuredSourceLanguages,
        bool allowCaptionFallback,
        IReadOnlyList<string>? targetLanguages = null,
        CancellationToken cancellationToken = default)
    {
        var isAutoMode = targetLanguages != null && targetLanguages.Count > 0;

        var normalizedSourceLanguages = configuredSourceLanguages
            .Select(SubtitleLanguageHelper.NormalizeLanguageCode)
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var assessments = new List<SubtitleSourceCandidateAssessment>();
        
        // In auto mode, accept all candidates (don't require configured languages to be set)
        if (candidates.Count == 0)
        {
            return new SubtitleSourceSelectionResult { Assessments = assessments };
        }
        
        if (!isAutoMode && normalizedSourceLanguages.Count == 0)
        {
            return new SubtitleSourceSelectionResult { Assessments = assessments };
        }

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            assessments.Add(await AssessCandidateAsync(
                candidate,
                normalizedSourceLanguages,
                isAutoMode,
                targetLanguages,
                cancellationToken));
        }

        var primary = assessments
            .Where(assessment => assessment.Role == SubtitleSourceCandidateRole.PrimaryFullDialogue)
            .OrderByDescending(assessment => assessment.Score)
            .ThenBy(assessment => assessment.Subtitle.StreamIndex)
            .FirstOrDefault();

        if (primary != null)
        {
            return CreateResult(primary, assessments);
        }

        var fallback = assessments
            .Where(assessment =>
                assessment.Role == SubtitleSourceCandidateRole.PathologicalAssFallback ||
                (allowCaptionFallback && assessment.Role == SubtitleSourceCandidateRole.CaptionFallback))
            .OrderByDescending(assessment => assessment.Score)
            .ThenBy(assessment => assessment.Subtitle.StreamIndex)
            .FirstOrDefault();

        if (fallback != null)
        {
            return CreateResult(fallback, assessments);
        }

        return new SubtitleSourceSelectionResult { Assessments = assessments };
    }

    private async Task<SubtitleSourceCandidateAssessment> AssessCandidateAsync(
        EmbeddedSubtitle candidate,
        IReadOnlyList<string> normalizedSourceLanguages,
        bool isAutoMode,
        IReadOnlyList<string>? targetLanguages = null,
        CancellationToken cancellationToken = default)
    {
        var matchedLanguage = isAutoMode
            ? GetCandidateLanguage(candidate)
            : MatchConfiguredLanguage(candidate, normalizedSourceLanguages);

        if (string.IsNullOrWhiteSpace(matchedLanguage))
        {
            return new SubtitleSourceCandidateAssessment(
                candidate,
                candidate.IsReadableSource()
                    ? SubtitleSourceCandidateRole.RejectedLanguage
                    : SubtitleSourceCandidateRole.RejectedNonText,
                null,
                int.MinValue,
                null,
                "Language is not configured as a source language.");
        }

        // In auto mode, check translation quality score against target languages
        if (isAutoMode && targetLanguages != null && targetLanguages.Count > 0)
        {
            var bestScore = ScoreAgainstTargets(matchedLanguage, targetLanguages);
            if (bestScore < MinimumAutoFallbackScore)
            {
                return new SubtitleSourceCandidateAssessment(
                    candidate,
                    SubtitleSourceCandidateRole.RejectedLanguage,
                    matchedLanguage,
                    int.MinValue,
                    null,
                    $"Auto mode: language '{matchedLanguage}' scored {bestScore:F1} against targets, below minimum of {MinimumAutoFallbackScore}.");
            }
        }

        if (!candidate.IsReadableSource())
        {
            return new SubtitleSourceCandidateAssessment(
                candidate,
                SubtitleSourceCandidateRole.RejectedNonText,
                matchedLanguage,
                int.MinValue,
                null,
                "Subtitle stream is not text-based and has no usable OCR output.");
        }

        var entryCount = GetExtractedEntryCount(candidate);
        var subtitleType = SubtitleLanguageHelper.DetermineSubtitleType(candidate, entryCount);

        if (entryCount.HasValue &&
            entryCount.Value >= 0 &&
            entryCount.Value < SubtitleExtractionService.MinimumDialogueEntries)
        {
            return new SubtitleSourceCandidateAssessment(
                candidate,
                SubtitleSourceCandidateRole.RejectedSparse,
                matchedLanguage,
                int.MinValue + 3,
                entryCount,
                $"Extracted source has only {entryCount.Value} dialogue entries.");
        }

        var health = await GetSourceHealthAsync(candidate, cancellationToken);
        if (health?.Status == SubtitleSourceHealthStatus.CorruptText)
        {
            return new SubtitleSourceCandidateAssessment(
                candidate,
                SubtitleSourceCandidateRole.RejectedCorrupt,
                matchedLanguage,
                int.MinValue + 5,
                entryCount,
                health.Reason);
        }

        var pathologicalAdjustment = await GetPathologicalScoreAdjustmentAsync(
            candidate,
            cancellationToken);
        if (string.Equals(subtitleType, SubtitleLanguageHelper.TypeCommentary, StringComparison.OrdinalIgnoreCase))
        {
            return new SubtitleSourceCandidateAssessment(
                candidate,
                SubtitleSourceCandidateRole.RejectedCommentary,
                matchedLanguage,
                int.MinValue + 5,
                entryCount,
                "Commentary subtitle streams are not valid dialogue sources.");
        }

        if (SubtitleLanguageHelper.IsSupplementalSubtitleType(subtitleType))
        {
            return new SubtitleSourceCandidateAssessment(
                candidate,
                SubtitleSourceCandidateRole.SupplementalForcedSigns,
                matchedLanguage,
                BuildScore(candidate, matchedLanguage, normalizedSourceLanguages, isAutoMode, pathologicalAdjustment.ScoreAdjustment),
                entryCount,
                "Forced/signs/songs subtitles are supplemental and cannot be primary sources.");
        }

        if (pathologicalAdjustment.IsPathological)
        {
            return new SubtitleSourceCandidateAssessment(
                candidate,
                SubtitleSourceCandidateRole.PathologicalAssFallback,
                matchedLanguage,
                BuildScore(candidate, matchedLanguage, normalizedSourceLanguages, isAutoMode, pathologicalAdjustment.ScoreAdjustment),
                entryCount,
                "ASS/SSA analysis detected drawing-heavy, duplicated, or fragmented source content; retained as a fallback.");
        }

        if (SubtitleLanguageHelper.IsForcedDialogueType(subtitleType))
        {
            var forcedDialogueScore = BuildScore(
                candidate,
                matchedLanguage,
                normalizedSourceLanguages,
                isAutoMode,
                pathologicalAdjustment.ScoreAdjustment);

            return new SubtitleSourceCandidateAssessment(
                candidate,
                SubtitleSourceCandidateRole.PrimaryFullDialogue,
                matchedLanguage,
                forcedDialogueScore,
                entryCount,
                "Forced-disposition track reclassified as full-dialogue based on entry count and title.");
        }

        var score = BuildScore(
            candidate,
            matchedLanguage,
            normalizedSourceLanguages,
            isAutoMode,
            pathologicalAdjustment.ScoreAdjustment);

        if (SubtitleLanguageHelper.IsCaptionSubtitleType(subtitleType))
        {
            return new SubtitleSourceCandidateAssessment(
                candidate,
                SubtitleSourceCandidateRole.CaptionFallback,
                matchedLanguage,
                score - 50,
                entryCount,
                "Caption/SDH source is available only as a fallback.");
        }

        return new SubtitleSourceCandidateAssessment(
            candidate,
            SubtitleSourceCandidateRole.PrimaryFullDialogue,
            matchedLanguage,
            score,
            entryCount,
            "Clean full-dialogue source candidate.");
    }

    private string? MatchConfiguredLanguage(
        EmbeddedSubtitle candidate,
        IReadOnlyList<string> normalizedSourceLanguages)
    {
        if (string.IsNullOrWhiteSpace(candidate.Language))
        {
            return null;
        }

        foreach (var sourceLanguage in normalizedSourceLanguages)
        {
            if (SubtitleLanguageHelper.LanguageMatches(candidate.Language, sourceLanguage))
            {
                return sourceLanguage;
            }
        }

        return null;
    }

    private int BuildScore(
        EmbeddedSubtitle candidate,
        string matchedLanguage,
        IReadOnlyList<string> normalizedSourceLanguages,
        bool isAutoMode,
        int contentScoreAdjustment)
    {
        var score = SubtitleLanguageHelper.ScoreSubtitleCandidate(
            candidate,
            matchedLanguage,
            contentScoreAdjustment);

        // In auto mode, skip the configured-language priority bonus
        if (!isAutoMode)
        {
            var languageIndex = normalizedSourceLanguages
                .Select((language, index) => new { language, index })
                .FirstOrDefault(item => string.Equals(
                    item.language,
                    matchedLanguage,
                    StringComparison.OrdinalIgnoreCase))
                ?.index ?? normalizedSourceLanguages.Count;
            score += (normalizedSourceLanguages.Count - languageIndex) * LanguagePriorityBonus;
        }

        var normalizedFormat = candidate.GetReadableSourceFormat();
        if (normalizedFormat is ".srt" or ".vtt")
        {
            score += 20;
        }
        else if (normalizedFormat is ".ass" or ".ssa")
        {
            score -= 10;
        }

        return score;
    }

    /// <summary>
    /// Gets the candidate's language directly, without matching against configured languages.
    /// Used in auto mode when configured source languages should be ignored.
    /// </summary>
    private static string? GetCandidateLanguage(EmbeddedSubtitle candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.Language) ||
            candidate.Language.Equals("und", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return SubtitleLanguageHelper.NormalizeLanguageCode(candidate.Language);
    }

    /// <summary>
    /// Scores a language against all target languages using the translation quality scorer.
    /// Returns the best (highest) score found, or 0 if scoring is unavailable.
    /// </summary>
    private double ScoreAgainstTargets(string language, IReadOnlyList<string> targetLanguages)
    {
        if (_qualityScorer == null)
        {
            return MinimumAutoFallbackScore; // No scorer available — accept all candidates
        }

        var bestScore = 0.0;
        foreach (var target in targetLanguages)
        {
            var score = _qualityScorer.ScoreDirection(language, target);
            if (score.HasValue && score.Value > bestScore)
            {
                bestScore = score.Value;
            }
        }
        return bestScore;
    }

    private int? GetExtractedEntryCount(EmbeddedSubtitle candidate)
    {
        var readablePath = candidate.GetReadableSourcePath();
        if (string.IsNullOrWhiteSpace(readablePath) || !File.Exists(readablePath))
        {
            return null;
        }

        try
        {
            return SubtitleExtractionService.CountSubtitleEntries(readablePath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to count extracted subtitle entries for stream {StreamIndex} at {Path}",
                candidate.StreamIndex,
                readablePath);
            return null;
        }
    }

    private async Task<SubtitleSourceHealthAnalysis?> GetSourceHealthAsync(
        EmbeddedSubtitle candidate,
        CancellationToken cancellationToken)
    {
        var readablePath = candidate.GetReadableSourcePath();
        if (string.IsNullOrWhiteSpace(readablePath) || !File.Exists(readablePath))
        {
            return null;
        }

        try
        {
            var subtitles = await _subtitleService.ReadSubtitles(readablePath);
            cancellationToken.ThrowIfCancellationRequested();
            return SubtitleSourceHealthAnalyzer.Analyze(subtitles);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to analyze subtitle source health for stream {StreamIndex} at {Path}. Continuing with metadata heuristics.",
                candidate.StreamIndex,
                readablePath);
            return null;
        }
    }

    private async Task<(bool IsPathological, int ScoreAdjustment)> GetPathologicalScoreAdjustmentAsync(
        EmbeddedSubtitle candidate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(candidate.ExtractedPath) ||
            !File.Exists(candidate.ExtractedPath) ||
            !SubtitleOutputModeHelper.IsAssFormat(candidate.CodecName))
        {
            return (false, 0);
        }

        try
        {
            var analysis = await AssSubtitleSourceAnalyzer.AnalyzeExtractedSubtitleAsync(
                candidate,
                _subtitleService,
                cancellationToken);
            if (analysis == null)
            {
                return (false, 0);
            }

            if (analysis.IsPathological)
            {
                _logger.LogWarning(
                    "Penalizing embedded subtitle stream {StreamIndex} ({Title}) as pathological fallback: drawingEvents={DrawingEvents}, translatableEvents={TranslatableEvents}, duplicateRatio={DuplicateRatio:F2}, avgProviderChars={AverageChars:F2}",
                    candidate.StreamIndex,
                    candidate.Title ?? candidate.CodecName,
                    analysis.DrawingEvents,
                    analysis.TranslatableEvents,
                    analysis.DuplicateRatio,
                    analysis.AverageProviderCharsPerTranslatableCue);
            }

            return (analysis.IsPathological, analysis.ContentScoreAdjustment);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to analyze extracted subtitle stream {StreamIndex} at {Path}. Continuing with metadata heuristics.",
                candidate.StreamIndex,
                candidate.ExtractedPath);
            return (false, 0);
        }
    }

    private static SubtitleSourceSelectionResult CreateResult(
        SubtitleSourceCandidateAssessment selected,
        IReadOnlyList<SubtitleSourceCandidateAssessment> assessments)
    {
        return new SubtitleSourceSelectionResult
        {
            SelectedSubtitle = selected.Subtitle,
            MatchedLanguage = selected.MatchedLanguage ?? string.Empty,
            SelectedRole = selected.Role,
            Assessments = assessments
        };
    }
}
