using Lingarr.Core.Data;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.Subtitle;

/// <summary>
/// Scans completed translation requests for subtitle position misalignment.
/// Uses word-count heuristics to detect cascade shifts where the model
/// returned content for the wrong position (e.g., translated[N] contains
/// the translation that should be at source[N+1]).
/// </summary>
public class SubtitleAlignmentCheckService : ISubtitleAlignmentCheckService
{
    private readonly LingarrDbContext _dbContext;
    private readonly ISubtitleService _subtitleService;
    private readonly ILogger<SubtitleAlignmentCheckService> _logger;

    // A shift is reported when at least this many consecutive positions
    // score better for a non-zero offset than for offset 0.
    private const int MinConsecutiveForShift = 4;

    // The word-count ratio between translation and source must differ by
    // at least this much from 1.0 to count as a mismatch (avoid false positives
    // when source and translation happen to have similar word counts).
    private const double MismatchThreshold = 0.30;

    public SubtitleAlignmentCheckService(
        LingarrDbContext dbContext,
        ISubtitleService subtitleService,
        ILogger<SubtitleAlignmentCheckService> logger)
    {
        _dbContext = dbContext;
        _subtitleService = subtitleService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SubtitleAlignmentCheckSummary> ScanRecentCompletedTranslationsAsync(
        int maxRequests = 50,
        CancellationToken ct = default)
    {
        var summary = new SubtitleAlignmentCheckSummary();

        try
        {
            var requests = await _dbContext.TranslationRequests
                .Where(r => r.Status == TranslationStatus.Completed &&
                            !string.IsNullOrWhiteSpace(r.SubtitleToTranslate) &&
                            !string.IsNullOrWhiteSpace(r.TranslatedSubtitle))
                .OrderByDescending(r => r.CompletedAt)
                .Take(maxRequests)
                .ToListAsync(ct);

            summary.TotalScanned = requests.Count;

            foreach (var request in requests)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var result = await CheckRequestAlignmentAsync(request, ct);
                    if (result.ShiftDetected)
                    {
                        summary.ShiftsDetected++;
                        summary.Results.Add(result);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Alignment check failed for request {RequestId} ({Title})",
                        request.Id, request.Title);
                    summary.Errors.Add($"Request {request.Id} ({request.Title}): {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Alignment scan cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alignment scan failed");
            summary.Errors.Add($"Scan failed: {ex.Message}");
        }

        return summary;
    }

    private async Task<SubtitleAlignmentCheckResult> CheckRequestAlignmentAsync(
        Core.Entities.TranslationRequest request,
        CancellationToken ct)
    {
        var result = new SubtitleAlignmentCheckResult
        {
            RequestId = request.Id,
            Title = request.Title,
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            SourcePath = request.SubtitleToTranslate,
            TranslatedPath = request.TranslatedSubtitle
        };

        // Read both subtitle files
        List<Models.FileSystem.SubtitleItem> sourceItems;
        List<Models.FileSystem.SubtitleItem> translatedItems;

        try
        {
            sourceItems = await _subtitleService.ReadSubtitles(request.SubtitleToTranslate!);
            translatedItems = await _subtitleService.ReadSubtitles(request.TranslatedSubtitle!);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read subtitle files for request {RequestId}", request.Id);
            return result;
        }

        if (sourceItems.Count == 0 || translatedItems.Count == 0)
        {
            return result;
        }

        // Build word count profiles indexed by position
        var sourceWc = sourceItems
            .ToDictionary(
                item => item.Position,
                item => CountWords(GetText(item)));

        var translatedWc = translatedItems
            .ToDictionary(
                item => item.Position,
                item => CountWords(GetText(item)));

        // Also track exact text for echo detection
        var sourceText = sourceItems
            .ToDictionary(item => item.Position, GetText);
        var translatedText = translatedItems
            .ToDictionary(item => item.Position, GetText);

        // Check each position where both source and translation exist
        var commonPositions = sourceWc.Keys
            .Intersect(translatedWc.Keys)
            .OrderBy(p => p)
            .ToList();

        if (commonPositions.Count < MinConsecutiveForShift + 2)
        {
            return result;
        }

        // For each position, determine which offset gives the best word-count match
        // offset 0 = current, offset 1 = shifted forward by 1, offset -1 = shifted backward
        var positionOffsets = new Dictionary<int, int>();
        var positionScores = new Dictionary<int, double>();

        foreach (var pos in commonPositions)
        {
            var twc = translatedWc[pos];
            if (twc == 0) continue;

            int bestOffset = 0;
            double bestScore = double.MaxValue;

            // Check offsets -2, -1, 0, 1, 2
            for (int offset = -2; offset <= 2; offset++)
            {
                var neighborPos = pos + offset;
                if (!sourceWc.TryGetValue(neighborPos, out var swc) || swc == 0)
                    continue;

                var ratio = (double)twc / swc;
                // Score: how far from 1.0 is the ratio (clamped)
                var score = Math.Abs(ratio - 1.0);

                if (score < bestScore)
                {
                    bestScore = score;
                    bestOffset = offset;
                }
            }

            positionOffsets[pos] = bestOffset;
            positionScores[pos] = bestScore;

            // Check for exact echo (identical source and translated text)
            if (sourceText.TryGetValue(pos, out var srcTxt) &&
                translatedText.TryGetValue(pos, out var trnTxt) &&
                string.Equals(srcTxt?.Trim(), trnTxt?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                // Exact echo = strong signal of a skipped/shifted position
                if (positionScores[pos] < 0.05)
                {
                    positionOffsets[pos] = 99; // Mark as echo
                }
            }
        }

        // Find runs of non-zero offsets (shift runs)
        var runs = FindShiftRuns(positionOffsets, commonPositions);
        if (runs.Count == 0)
        {
            return result;
        }

        // Pick the longest/best run
        var bestRun = runs
            .OrderByDescending(r => r.Length)
            .ThenByDescending(r => r.AverageScore)
            .First();

        // Calculate confidence based on run length and average score
        var runLengthRatio = (double)bestRun.Length / commonPositions.Count;
        var avgMismatch = bestRun.Offsets.Average(o => Math.Abs(o));

        result.ShiftDetected = true;
        result.ShiftStartPosition = bestRun.StartPosition;
        result.ShiftMagnitude = (int)Math.Round(bestRun.Offsets.DefaultIfEmpty(0).Average(o => (double)o));
        result.ConsecutiveMismatches = bestRun.Length;
        result.Confidence = Math.Min(1.0, (runLengthRatio * 0.6) + (Math.Min(avgMismatch, 2.0) / 2.0 * 0.4));

        // Collect sample mismatches for the report
        result.Samples = bestRun.Positions
            .Take(5)
            .Select(pos =>
            {
                var src = Truncate(sourceText.GetValueOrDefault(pos, ""), 40);
                var trn = Truncate(translatedText.GetValueOrDefault(pos, ""), 40);
                var offset = positionOffsets.GetValueOrDefault(pos, 0);
                return $"Pos {pos}: offset={offset}, src=\"{src}\", trn=\"{trn}\"";
            })
            .ToList();

        _logger.LogWarning(
            "Alignment check: Shift detected in request {RequestId} ({Title}). " +
            "Start={Start}, magnitude={Mag}, consecutive={Count}, confidence={Conf:P0}. " +
            "Samples: {Samples}",
            request.Id, request.Title,
            result.ShiftStartPosition, result.ShiftMagnitude,
            result.ConsecutiveMismatches, result.Confidence,
            string.Join("; ", result.Samples));

        return result;
    }

    private static List<ShiftRun> FindShiftRuns(
        Dictionary<int, int> positionOffsets,
        List<int> sortedPositions)
    {
        var runs = new List<ShiftRun>();
        var currentRunPositions = new List<int>();
        var currentRunOffsets = new List<int>();

        foreach (var pos in sortedPositions)
        {
            if (!positionOffsets.TryGetValue(pos, out var offset))
                continue;

            // Skip echo markers and zero offsets
            if (offset == 99 || offset == 0)
            {
                // End current run if it exists
                if (currentRunPositions.Count >= MinConsecutiveForShift)
                {
                    runs.Add(BuildRun(currentRunPositions, currentRunOffsets));
                }
                currentRunPositions.Clear();
                currentRunOffsets.Clear();
                continue;
            }

            // Check if this continues a consistent offset
            if (currentRunPositions.Count > 0 &&
                Math.Abs(offset - currentRunOffsets.LastOrDefault()) > 1 &&
                currentRunOffsets.All(o => o != offset))
            {
                // Breaking pattern - if the current run is long enough, save it
                if (currentRunPositions.Count >= MinConsecutiveForShift)
                {
                    runs.Add(BuildRun(currentRunPositions, currentRunOffsets));
                }
                currentRunPositions.Clear();
                currentRunOffsets.Clear();
            }

            currentRunPositions.Add(pos);
            currentRunOffsets.Add(offset);
        }

        // Check final run
        if (currentRunPositions.Count >= MinConsecutiveForShift)
        {
            runs.Add(BuildRun(currentRunPositions, currentRunOffsets));
        }

        return runs;
    }

    private static ShiftRun BuildRun(List<int> positions, List<int> offsets)
    {
        return new ShiftRun
        {
            StartPosition = positions[0],
            Length = positions.Count,
            Positions = [..positions],
            Offsets = [..offsets],
            AverageScore = offsets.Average(o => Math.Abs((double)o))
        };
    }

    private static string GetText(Models.FileSystem.SubtitleItem item)
    {
        if (item.PlaintextLines.Count > 0)
        {
            return string.Join(" ", item.PlaintextLines);
        }

        return string.Join(" ", item.Lines);
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        return text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static string Truncate(string? text, int maxLen)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        return text.Length <= maxLen ? text : text[..(maxLen - 3)] + "...";
    }

    private sealed class ShiftRun
    {
        public int StartPosition { get; set; }
        public int Length { get; set; }
        public List<int> Positions { get; set; } = [];
        public List<int> Offsets { get; set; } = [];
        public double AverageScore { get; set; }
    }
}
