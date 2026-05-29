using Lingarr.Server.Models.Batch;
using Lingarr.Server.Models.Batch.Response;
using System.Security.Cryptography;
using System.Text;

namespace Lingarr.Server.Services.Translation;

internal sealed class SafeMappingResult
{
    /// <summary>
    /// Translations that passed sourceKey validation, keyed by position.
    /// </summary>
    public required Dictionary<int, string> ValidTranslations { get; init; }
    
    /// <summary>
    /// Positions where the provider returned content whose sourceKey does not match
    /// the requested sourceKey. The provider likely returned wrong/shifted content.
    /// Callers should preserve the original source text for these positions.
    /// </summary>
    public required List<int> SourceKeyFailures { get; init; }
    
    /// <summary>
    /// Positions that were in the request but missing from the response entirely.
    /// </summary>
    public required List<int> MissingPositions { get; init; }
    
    /// <summary>
    /// Positions in the response that were not in the request (provider hallucination).
    /// </summary>
    public required List<int> UnexpectedPositions { get; init; }
    
    /// <summary>
    /// Positions that appeared more than once in the provider response.
    /// </summary>
    public required List<int> DuplicatePositions { get; init; }
}

internal static class BatchTranslationResponseMapper
{
    /// <summary>
    /// Maps translated items back to request items using position as the key.
    /// Uses lenient sourceKey validation: non-matching sourceKeys are logged
    /// but the translation is still accepted if the line is non-empty.
    /// </summary>
    public static Dictionary<int, string> MapAlignedTranslations(
        IReadOnlyList<BatchSubtitleItem> requestedItems,
        IReadOnlyList<StructuredBatchResponse> translatedItems,
        ILogger logger,
        string providerName)
    {
        var requestedByPosition = requestedItems.ToDictionary(item => item.Position);
        var mapped = new Dictionary<int, string>();
        var duplicatePositions = new HashSet<int>();
        var unexpectedPositions = new List<int>();
        var sourceKeyMismatchedPositions = new List<int>();

        foreach (var item in translatedItems)
        {
            if (!requestedByPosition.TryGetValue(item.Position, out var requested))
            {
                unexpectedPositions.Add(item.Position);
                continue;
            }

            var requestedSourceKey = GetSourceKey(requested);
            var sourceKeyOk = !string.IsNullOrWhiteSpace(item.SourceKey) &&
                string.Equals(item.SourceKey, requestedSourceKey, StringComparison.Ordinal);

            if (!sourceKeyOk)
            {
                if (string.IsNullOrWhiteSpace(item.Line))
                {
                    sourceKeyMismatchedPositions.Add(item.Position);
                    continue;
                }

                // Lenient mode: accept non-empty line even when sourceKey doesn't match
                sourceKeyMismatchedPositions.Add(item.Position);
            }

            if (!mapped.TryAdd(item.Position, item.Line))
            {
                duplicatePositions.Add(item.Position);
            }
        }

        var missingPositions = requestedByPosition.Keys
            .Except(mapped.Keys)
            .OrderBy(position => position)
            .ToList();

        if (missingPositions.Count > 0 ||
            unexpectedPositions.Count > 0 ||
            duplicatePositions.Count > 0 ||
            sourceKeyMismatchedPositions.Count > 0)
        {
            var sourceSamples = requestedItems
                .Where(item => missingPositions.Contains(item.Position))
                .Take(5)
                .Select(item => $"{item.Position}: {item.Line}")
                .ToList();

            logger.LogWarning(
                "{Provider} batch response did not align exactly with the request. Missing={MissingCount} [{MissingPositions}], unexpected={UnexpectedCount} [{UnexpectedPositions}], duplicates={DuplicateCount} [{DuplicatePositions}], sourceKeyMismatches={MismatchCount} [{MismatchPositions}]. Missing samples: {Samples}",
                providerName,
                missingPositions.Count,
                string.Join(", ", missingPositions.Take(10)),
                unexpectedPositions.Count,
                string.Join(", ", unexpectedPositions.Distinct().OrderBy(position => position).Take(10)),
                duplicatePositions.Count,
                string.Join(", ", duplicatePositions.OrderBy(position => position).Take(10)),
                sourceKeyMismatchedPositions.Count,
                string.Join(", ", sourceKeyMismatchedPositions.OrderBy(position => position).Take(10)),
                string.Join(" | ", sourceSamples));
        }

        return mapped;
    }

    /// <summary>
    /// Maps translated items back to request items with STRICT sourceKey validation.
    /// Unlike MapAlignedTranslations, this REJECTS items whose sourceKey doesn't match
    /// the requested sourceKey. SourceKey mismatch indicates the model returned content
    /// for the wrong position (a shift/misalignment). These are NOT transient errors
    /// and should NOT be retried -- callers should preserve the original source text.
    /// </summary>
    /// <param name="requestedItems">The items sent to the provider.</param>
    /// <param name="translatedItems">The items returned by the provider.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="providerName">Provider name for log messages.</param>
    /// <returns>A SafeMappingResult with validated translations and sourceKey failures separated.</returns>
    public static SafeMappingResult MapAlignedTranslationsSafe(
        IReadOnlyList<BatchSubtitleItem> requestedItems,
        IReadOnlyList<StructuredBatchResponse> translatedItems,
        ILogger logger,
        string providerName)
    {
        var requestedByPosition = requestedItems.ToDictionary(item => item.Position);
        var valid = new Dictionary<int, string>();
        var sourceKeyFailures = new List<int>();
        var missingPositions = new List<int>();
        var unexpectedPositions = new List<int>();
        var duplicatePositions = new HashSet<int>();

        foreach (var item in translatedItems)
        {
            if (!requestedByPosition.TryGetValue(item.Position, out var requested))
            {
                unexpectedPositions.Add(item.Position);
                continue;
            }

            var requestedSourceKey = GetSourceKey(requested);
            var sourceKeyOk = !string.IsNullOrWhiteSpace(item.SourceKey) &&
                string.Equals(item.SourceKey, requestedSourceKey, StringComparison.Ordinal);

            if (!sourceKeyOk)
            {
                // STRICT mode: reject. The model returned content for a different
                // source line than the one at this position. This is a misalignment.
                sourceKeyFailures.Add(item.Position);
                LogSourceKeyMismatchDetail(logger, providerName, item.Position, 
                    requested.Line, item.Line, requestedSourceKey, item.SourceKey);
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.Line))
            {
                missingPositions.Add(item.Position);
                continue;
            }

            if (!valid.TryAdd(item.Position, item.Line))
            {
                duplicatePositions.Add(item.Position);
            }
        }

        // Find positions that were in the request but never accounted for
        var trulyMissing = requestedByPosition.Keys
            .Where(p => !valid.ContainsKey(p) && 
                        !sourceKeyFailures.Contains(p) && 
                        !missingPositions.Contains(p) &&
                        !unexpectedPositions.Contains(p))
            .ToList();
        missingPositions.AddRange(trulyMissing);

        LogSafeMappingDiagnostics(logger, providerName, 
            missingPositions, unexpectedPositions, 
            duplicatePositions.ToList(), sourceKeyFailures, requestedItems);

        return new SafeMappingResult
        {
            ValidTranslations = valid,
            SourceKeyFailures = sourceKeyFailures,
            MissingPositions = missingPositions,
            UnexpectedPositions = unexpectedPositions.Distinct().ToList(),
            DuplicatePositions = duplicatePositions.ToList()
        };
    }

    public static string GetSourceKey(BatchSubtitleItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.SourceKey))
        {
            return item.SourceKey;
        }

        var normalizedLine = item.Line.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        var payload = $"{item.Position}\n{normalizedLine}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hashBytes)[..12].ToLowerInvariant();
    }

    private static void LogSourceKeyMismatchDetail(
        ILogger logger,
        string providerName,
        int position,
        string requestedLine,
        string? returnedLine,
        string expectedKey,
        string? actualKey)
    {
        var returnedPreview = (returnedLine ?? "<null>").Length > 80
            ? (returnedLine ?? "<null>")[..77] + "..."
            : (returnedLine ?? "<null>");
        var requestedPreview = requestedLine.Length > 80
            ? requestedLine[..77] + "..."
            : requestedLine;

        logger.LogWarning(
            "{Provider} sourceKey mismatch at position {Position}. " +
            "Expected sourceKey={ExpectedKey}, actual sourceKey={ActualKey}. " +
            "Source text: \"{SourceText}\". " +
            "Provider returned: \"{ReturnedText}\". " +
            "This indicates the model returned content for the wrong position. " +
            "Source text will be preserved for this position.",
            providerName,
            position,
            expectedKey,
            actualKey ?? "<missing>",
            requestedPreview,
            returnedPreview);
    }

    private static void LogSafeMappingDiagnostics(
        ILogger logger,
        string providerName,
        List<int> missingPositions,
        List<int> unexpectedPositions,
        List<int> duplicatePositions,
        List<int> sourceKeyFailures,
        IReadOnlyList<BatchSubtitleItem> requestedItems)
    {
        if (missingPositions.Count == 0 && 
            unexpectedPositions.Count == 0 && 
            duplicatePositions.Count == 0 && 
            sourceKeyFailures.Count == 0)
        {
            return;
        }

        var sourceSamples = requestedItems
            .Where(item => missingPositions.Contains(item.Position))
            .Take(5)
            .Select(item => $"{item.Position}: {item.Line}")
            .ToList();

        logger.LogWarning(
            "{Provider} batch response alignment issues: " +
            "Missing={MissingCount} [{MissingPositions}], " +
            "Unexpected={UnexpectedCount} [{UnexpectedPositions}], " +
            "Duplicates={DuplicateCount} [{DuplicatePositions}], " +
            "sourceKeyMismatches={SourceKeyCount} [{SourceKeyPositions}] (REJECTED). " +
            "Missing samples: {Samples}",
            providerName,
            missingPositions.Count,
            string.Join(", ", missingPositions.Take(10)),
            unexpectedPositions.Count,
            string.Join(", ", unexpectedPositions.Take(10)),
            duplicatePositions.Count,
            string.Join(", ", duplicatePositions.Take(10)),
            sourceKeyFailures.Count,
            string.Join(", ", sourceKeyFailures.Take(10)),
            string.Join(" | ", sourceSamples));
    }
}
