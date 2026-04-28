using System.Text.RegularExpressions;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Subtitle;

internal static class AssVerificationIssueTypes
{
    public const string DrawingArtifact = "drawing_artifact";
    public const string UnexpectedAssTags = "unexpected_ass_tags";
    public const string AssTagMismatch = "ass_tag_mismatch";
    public const string InlineAssTagPlacement = "inline_ass_tag_placement";
    public const string UnchangedSourceText = "unchanged_source_text";
    public const string TargetLanguageMismatch = "target_language_mismatch";
}

internal sealed class AssArtifactScanResult
{
    public int SuspiciousLineCount { get; set; }
    public List<string> SuspiciousLines { get; set; } = [];
    public List<string> IssueTypes { get; set; } = [];
    public List<string> IssueSummaries { get; set; } = [];
    public bool HasIssues => IssueTypes.Count > 0;

    public void Merge(AssArtifactScanResult other)
    {
        SuspiciousLineCount += other.SuspiciousLineCount;
        SuspiciousLines.AddRange(other.SuspiciousLines);

        foreach (var issueType in other.IssueTypes)
        {
            if (!IssueTypes.Contains(issueType, StringComparer.Ordinal))
            {
                IssueTypes.Add(issueType);
            }
        }

        foreach (var summary in other.IssueSummaries)
        {
            if (!IssueSummaries.Contains(summary, StringComparer.Ordinal))
            {
                IssueSummaries.Add(summary);
            }
        }
    }
}

internal static class AssSubtitleArtifactDetector
{
    private const int SuspiciousLinePreviewLimit = 10;
    private const int SuspiciousPreviewLength = 80;
    private const int DrawingSuspiciousThreshold = 2;

    private static readonly Regex DrawingPattern = new(
        @"^\s*m\s+-?\d+(\.\d+)?\s+-?\d+(\.\d+)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AssSignaturePattern = new(
        @"\{\\[^}]*\}|\\[Nnh]",
        RegexOptions.Compiled);

    private static readonly Regex InlineStyleTagBetweenWordCharactersPattern = new(
        @"(?<=[\p{L}\p{N}])\{\\(?:i[01]?|b[01]?|u[01]?|s[01]?|bord|shad|fs|fn)[^}]*\}(?=[\p{L}\p{N}])",
        RegexOptions.Compiled);

    public static AssArtifactScanResult DetectDrawingArtifacts(IEnumerable<string> lines)
    {
        var suspiciousLines = lines
            .Where(line => DrawingPattern.IsMatch(line.Trim()))
            .Select(Truncate)
            .ToList();

        var result = new AssArtifactScanResult
        {
            SuspiciousLineCount = suspiciousLines.Count,
            SuspiciousLines = suspiciousLines.Take(SuspiciousLinePreviewLimit).ToList()
        };

        if (suspiciousLines.Count >= DrawingSuspiciousThreshold)
        {
            result.IssueTypes.Add(AssVerificationIssueTypes.DrawingArtifact);
            result.IssueSummaries.Add($"Found {suspiciousLines.Count} ASS drawing artifact lines.");
        }

        return result;
    }

    public static AssArtifactScanResult DetectInlineTagPlacementArtifacts(IEnumerable<string> lines)
    {
        var suspiciousLines = lines
            .Where(line => InlineStyleTagBetweenWordCharactersPattern.IsMatch(line))
            .Select(Truncate)
            .ToList();

        var result = new AssArtifactScanResult
        {
            SuspiciousLineCount = suspiciousLines.Count,
            SuspiciousLines = suspiciousLines.Take(SuspiciousLinePreviewLimit).ToList()
        };

        if (suspiciousLines.Count > 0)
        {
            result.IssueTypes.Add(AssVerificationIssueTypes.InlineAssTagPlacement);
            result.IssueSummaries.Add($"Found {suspiciousLines.Count} ASS/SSA inline style tags placed inside words.");
        }

        return result;
    }

    public static AssArtifactScanResult CompareTagStructure(
        IReadOnlyList<SubtitleItem> sourceSubtitles,
        IReadOnlyList<SubtitleItem> targetSubtitles,
        string targetSubtitlePath)
    {
        if (SubtitleOutputModeHelper.IsAssFormat(Path.GetExtension(targetSubtitlePath)))
        {
            return new AssArtifactScanResult();
        }

        var sourceByPosition = sourceSubtitles
            .GroupBy(item => item.Position)
            .ToDictionary(group => group.Key, group => group.First());
        var result = new AssArtifactScanResult();
        var unexpectedCount = 0;
        var mismatchCount = 0;

        for (var targetIndex = 0; targetIndex < targetSubtitles.Count; targetIndex++)
        {
            var target = targetSubtitles[targetIndex];
            var source = sourceByPosition.TryGetValue(target.Position, out var matchedSource)
                ? matchedSource
                : targetIndex < sourceSubtitles.Count
                    ? sourceSubtitles[targetIndex]
                    : null;

            if (source == null)
            {
                continue;
            }

            var sourceSignature = ExtractAssSignature(source.Lines);
            var targetSignature = ExtractAssSignature(target.Lines);

            if (targetSignature.Count == 0 && sourceSignature.Count == 0)
            {
                continue;
            }

            if (targetSignature.Count > 0 && sourceSignature.Count == 0)
            {
                unexpectedCount++;
                AddSuspiciousLine(result, target);
                continue;
            }

            if (!sourceSignature.SequenceEqual(targetSignature, StringComparer.Ordinal))
            {
                mismatchCount++;
                AddSuspiciousLine(result, target);
            }
        }

        if (unexpectedCount > 0)
        {
            result.IssueTypes.Add(AssVerificationIssueTypes.UnexpectedAssTags);
            result.IssueSummaries.Add($"Found {unexpectedCount} translated entries with ASS/SSA tags not present in the source.");
        }

        if (mismatchCount > 0)
        {
            result.IssueTypes.Add(AssVerificationIssueTypes.AssTagMismatch);
            result.IssueSummaries.Add($"Found {mismatchCount} translated entries with ASS/SSA tag structure that differs from the source.");
        }

        return result;
    }

    private static List<string> ExtractAssSignature(IEnumerable<string> lines)
    {
        return lines
            .SelectMany(line => AssSignaturePattern.Matches(line).Select(match => match.Value))
            .ToList();
    }

    private static void AddSuspiciousLine(AssArtifactScanResult result, SubtitleItem item)
    {
        result.SuspiciousLineCount++;

        if (result.SuspiciousLines.Count >= SuspiciousLinePreviewLimit)
        {
            return;
        }

        result.SuspiciousLines.Add(Truncate(string.Join(" ", item.Lines)));
    }

    private static string Truncate(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length > SuspiciousPreviewLength
            ? trimmed[..SuspiciousPreviewLength] + "..."
            : trimmed;
    }
}
