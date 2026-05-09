using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;

namespace Lingarr.Server.Services.Subtitle;

public class SubtitleSourceSelectionService : ISubtitleSourceSelectionService
{
    private const int LanguagePriorityBonus = 20;
    private readonly ISubtitleService _subtitleService;
    private readonly ILogger<SubtitleSourceSelectionService> _logger;

    public SubtitleSourceSelectionService(
        ISubtitleService subtitleService,
        ILogger<SubtitleSourceSelectionService> logger)
    {
        _subtitleService = subtitleService;
        _logger = logger;
    }

    public async Task<SubtitleSourceSelectionResult> SelectPrimaryAsync(
        IReadOnlyCollection<EmbeddedSubtitle> candidates,
        IReadOnlyList<string> configuredSourceLanguages,
        bool allowCaptionFallback,
        CancellationToken cancellationToken = default)
    {
        var normalizedSourceLanguages = configuredSourceLanguages
            .Select(SubtitleLanguageHelper.NormalizeLanguageCode)
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var assessments = new List<SubtitleSourceCandidateAssessment>();
        if (candidates.Count == 0 || normalizedSourceLanguages.Count == 0)
        {
            return new SubtitleSourceSelectionResult { Assessments = assessments };
        }

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            assessments.Add(await AssessCandidateAsync(
                candidate,
                normalizedSourceLanguages,
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

        if (allowCaptionFallback)
        {
            var captionFallback = assessments
                .Where(assessment => assessment.Role == SubtitleSourceCandidateRole.CaptionFallback)
                .OrderByDescending(assessment => assessment.Score)
                .ThenBy(assessment => assessment.Subtitle.StreamIndex)
                .FirstOrDefault();

            if (captionFallback != null)
            {
                return CreateResult(captionFallback, assessments);
            }
        }

        return new SubtitleSourceSelectionResult { Assessments = assessments };
    }

    private async Task<SubtitleSourceCandidateAssessment> AssessCandidateAsync(
        EmbeddedSubtitle candidate,
        IReadOnlyList<string> normalizedSourceLanguages,
        CancellationToken cancellationToken)
    {
        var matchedLanguage = MatchConfiguredLanguage(candidate, normalizedSourceLanguages);
        if (string.IsNullOrWhiteSpace(matchedLanguage))
        {
            return new SubtitleSourceCandidateAssessment(
                candidate,
                candidate.IsTextBased
                    ? SubtitleSourceCandidateRole.RejectedLanguage
                    : SubtitleSourceCandidateRole.RejectedNonText,
                null,
                int.MinValue,
                null,
                "Language is not configured as a source language.");
        }

        if (!candidate.IsTextBased)
        {
            return new SubtitleSourceCandidateAssessment(
                candidate,
                SubtitleSourceCandidateRole.RejectedNonText,
                matchedLanguage,
                int.MinValue,
                null,
                "Subtitle stream is not text-based.");
        }

        var subtitleType = SubtitleLanguageHelper.DetermineSubtitleType(candidate);
        var entryCount = GetExtractedEntryCount(candidate);

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
        if (pathologicalAdjustment.IsPathological)
        {
            return new SubtitleSourceCandidateAssessment(
                candidate,
                SubtitleSourceCandidateRole.RejectedPathological,
                matchedLanguage,
                int.MinValue + 4,
                entryCount,
                "ASS/SSA analysis detected drawing-heavy, duplicated, or fragmented source content.");
        }

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
                BuildScore(candidate, matchedLanguage, normalizedSourceLanguages, pathologicalAdjustment.ScoreAdjustment),
                entryCount,
                "Forced/signs/songs subtitles are supplemental and cannot be primary sources.");
        }

        var score = BuildScore(
            candidate,
            matchedLanguage,
            normalizedSourceLanguages,
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
        int contentScoreAdjustment)
    {
        var score = SubtitleLanguageHelper.ScoreSubtitleCandidate(
            candidate,
            matchedLanguage,
            contentScoreAdjustment);

        var languageIndex = normalizedSourceLanguages
            .Select((language, index) => new { language, index })
            .FirstOrDefault(item => string.Equals(
                item.language,
                matchedLanguage,
                StringComparison.OrdinalIgnoreCase))
            ?.index ?? normalizedSourceLanguages.Count;
        score += (normalizedSourceLanguages.Count - languageIndex) * LanguagePriorityBonus;

        var normalizedFormat = SubtitleOutputModeHelper.NormalizeFormat(candidate.CodecName);
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

    private int? GetExtractedEntryCount(EmbeddedSubtitle candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.ExtractedPath) || !File.Exists(candidate.ExtractedPath))
        {
            return null;
        }

        try
        {
            return SubtitleExtractionService.CountSubtitleEntries(candidate.ExtractedPath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to count extracted subtitle entries for stream {StreamIndex} at {Path}",
                candidate.StreamIndex,
                candidate.ExtractedPath);
            return null;
        }
    }

    private async Task<SubtitleSourceHealthAnalysis?> GetSourceHealthAsync(
        EmbeddedSubtitle candidate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(candidate.ExtractedPath) || !File.Exists(candidate.ExtractedPath))
        {
            return null;
        }

        try
        {
            var subtitles = await _subtitleService.ReadSubtitles(candidate.ExtractedPath);
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
                candidate.ExtractedPath);
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
                    "Rejecting embedded subtitle stream {StreamIndex} ({Title}) as pathological: drawingEvents={DrawingEvents}, translatableEvents={TranslatableEvents}, duplicateRatio={DuplicateRatio:F2}, avgProviderChars={AverageChars:F2}",
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
