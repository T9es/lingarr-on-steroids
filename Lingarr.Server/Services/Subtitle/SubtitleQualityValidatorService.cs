using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Translation;

namespace Lingarr.Server.Services.Subtitle;

public class SubtitleQualityValidatorService : ISubtitleQualityValidatorService
{
    private const double MinimumTargetRatio = 0.80;
    private const double MaximumTargetRatio = 1.50;
    private const int MaximumTargetRatioMinimumSourceEntries = 20;

    private readonly ISubtitleService _subtitleService;
    private readonly ILogger<SubtitleQualityValidatorService> _logger;

    public SubtitleQualityValidatorService(
        ISubtitleService subtitleService,
        ILogger<SubtitleQualityValidatorService> logger)
    {
        _subtitleService = subtitleService;
        _logger = logger;
    }

    public async Task<SubtitleQualityValidationResult> ValidateAsync(
        SubtitleQualityValidationRequest request,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(request.SourcePath))
        {
            return Invalid(
                SubtitleQualityIssueCodes.MissingSource,
                $"Source subtitle does not exist: {request.SourcePath}");
        }

        if (!File.Exists(request.TargetPath))
        {
            return Invalid(
                SubtitleQualityIssueCodes.MissingTarget,
                $"Target subtitle does not exist: {request.TargetPath}");
        }

        try
        {
            var sourceSubtitles = await _subtitleService.ReadSubtitles(request.SourcePath);
            var targetSubtitles = await _subtitleService.ReadSubtitles(request.TargetPath);
            var sourceCount = sourceSubtitles.Count;
            var targetCount = targetSubtitles.Count;
            var minimumTargetCount = (int)(sourceCount * MinimumTargetRatio);

            if (sourceCount == 0)
            {
                return Invalid(
                    SubtitleQualityIssueCodes.EmptySource,
                    "Source subtitle has no dialogue entries.",
                    sourceCount,
                    targetCount,
                    minimumTargetCount);
            }

            var issueTypes = new List<string>();
            var summaries = new List<string>();
            var samples = new List<string>();

            if (targetCount < minimumTargetCount)
            {
                var missingSourceEntries = GetMissingSourceEntries(sourceSubtitles, targetSubtitles);
                issueTypes.Add(SubtitleQualityIssueCodes.TooShort);
                summaries.Add($"Target has {targetCount} entries but selected source has {sourceCount}; minimum acceptable is {minimumTargetCount}.");
                if (missingSourceEntries.Count > 0)
                {
                    summaries.Add(
                        $"Missing source positions: {string.Join(", ", missingSourceEntries.Take(20).Select(item => item.Position))}.");
                    samples.AddRange(missingSourceEntries
                        .Take(10)
                        .Select(item => $"{item.Position}: {string.Join(" ", item.PlaintextLines.Count > 0 ? item.PlaintextLines : item.Lines)}"));
                }
            }

            if (sourceCount >= MaximumTargetRatioMinimumSourceEntries &&
                targetCount > (int)Math.Ceiling(sourceCount * MaximumTargetRatio))
            {
                issueTypes.Add(SubtitleQualityIssueCodes.TooLong);
                summaries.Add($"Target has {targetCount} entries but selected source has {sourceCount}; this is suspiciously high.");
            }

            var echoScan = DetectUnchangedSourceText(
                sourceSubtitles,
                targetSubtitles,
                request.SourceLanguage,
                request.TargetLanguage);
            MergeScan(issueTypes, summaries, samples, echoScan);

            var languageScan = DetectWrongTargetLanguage(
                sourceSubtitles,
                targetSubtitles,
                request.TargetLanguage);
            MergeScan(issueTypes, summaries, samples, languageScan);

            var artifactScan = new AssArtifactScanResult();
            if (!SubtitleOutputModeHelper.IsAssFormat(request.OutputFormat ?? Path.GetExtension(request.TargetPath)))
            {
                artifactScan.Merge(AssSubtitleArtifactDetector.DetectUnexpectedAssTagsInPlainTextOutput(
                    targetSubtitles.SelectMany(item => item.Lines)));
                artifactScan.Merge(AssSubtitleArtifactDetector.DetectDrawingArtifacts(
                    targetSubtitles.SelectMany(item => item.Lines),
                    suspiciousThreshold: 1));
            }
            MergeScan(issueTypes, summaries, samples, artifactScan);

            if (issueTypes.Count > 0)
            {
                var summary = string.Join(" ", summaries.Distinct(StringComparer.Ordinal));
                return new SubtitleQualityValidationResult
                {
                    IsValid = false,
                    Summary = summary,
                    SourceEntryCount = sourceCount,
                    TargetEntryCount = targetCount,
                    MinimumTargetEntryCount = minimumTargetCount,
                    IssueTypes = issueTypes.Distinct(StringComparer.Ordinal).ToList(),
                    SampleLines = samples.Distinct(StringComparer.Ordinal).Take(10).ToList()
                };
            }

            return new SubtitleQualityValidationResult
            {
                IsValid = true,
                Summary = "Subtitle output passed quality validation.",
                SourceEntryCount = sourceCount,
                TargetEntryCount = targetCount,
                MinimumTargetEntryCount = minimumTargetCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Subtitle quality validation failed unexpectedly for {TargetPath}",
                request.TargetPath);
            return Invalid(
                SubtitleQualityIssueCodes.ValidationError,
                $"Subtitle quality validation failed unexpectedly: {ex.Message}");
        }
    }

    private static AssArtifactScanResult DetectUnchangedSourceText(
        IReadOnlyList<SubtitleItem> sourceSubtitles,
        IReadOnlyList<SubtitleItem> targetSubtitles,
        string? sourceLanguage,
        string? targetLanguage)
    {
        var analysis = TranslationEchoGuard.AnalyzeSubtitles(
            sourceSubtitles,
            targetSubtitles,
            sourceLanguage,
            targetLanguage);
        if (!analysis.IsMostlyEchoed)
        {
            return new AssArtifactScanResult();
        }

        return new AssArtifactScanResult
        {
            SuspiciousLineCount = analysis.EchoedCount,
            SuspiciousLines = analysis.Samples.ToList(),
            IssueTypes = [SubtitleQualityIssueCodes.UnchangedSourceText],
            IssueSummaries =
            [
                $"Found mostly unchanged source text in translated output ({analysis.EchoedCount}/{analysis.ComparableCount} comparable cues; strongest cluster {analysis.ClusterEchoedCount}/{analysis.ClusterComparableCount})."
            ]
        };
    }

    private static AssArtifactScanResult DetectWrongTargetLanguage(
        IReadOnlyList<SubtitleItem> sourceSubtitles,
        IReadOnlyList<SubtitleItem> targetSubtitles,
        string? targetLanguage)
    {
        var analysis = TranslationLanguageGuard.AnalyzeSubtitles(
            sourceSubtitles,
            targetSubtitles,
            targetLanguage);
        if (!analysis.IsMostlyMismatched)
        {
            return new AssArtifactScanResult();
        }

        return new AssArtifactScanResult
        {
            SuspiciousLineCount = analysis.MismatchedCount,
            SuspiciousLines = analysis.Samples.ToList(),
            IssueTypes = [SubtitleQualityIssueCodes.TargetLanguageMismatch],
            IssueSummaries =
            [
                $"Target appears to use the wrong language/script. Expected {analysis.ExpectedDescription}, observed {analysis.ObservedDescription}; mismatched {analysis.MismatchedCount}/{analysis.ComparableCount} comparable cues."
            ]
        };
    }

    private static void MergeScan(
        List<string> issueTypes,
        List<string> summaries,
        List<string> samples,
        AssArtifactScanResult scan)
    {
        if (!scan.HasIssues)
        {
            return;
        }

        issueTypes.AddRange(scan.IssueTypes);
        summaries.AddRange(scan.IssueSummaries);
        samples.AddRange(scan.SuspiciousLines);
    }

    private static SubtitleQualityValidationResult Invalid(
        string issueType,
        string summary,
        int sourceCount = 0,
        int targetCount = 0,
        int minimumTargetCount = 0) => new()
    {
        IsValid = false,
        Summary = summary,
        SourceEntryCount = sourceCount,
        TargetEntryCount = targetCount,
        MinimumTargetEntryCount = minimumTargetCount,
        IssueTypes = [issueType]
    };

    private static List<SubtitleItem> GetMissingSourceEntries(
        IReadOnlyList<SubtitleItem> sourceSubtitles,
        IReadOnlyList<SubtitleItem> targetSubtitles)
    {
        var targetPositions = targetSubtitles
            .Select(item => item.Position)
            .ToHashSet();

        return sourceSubtitles
            .Where(item => !targetPositions.Contains(item.Position))
            .OrderBy(item => item.Position)
            .ToList();
    }
}
