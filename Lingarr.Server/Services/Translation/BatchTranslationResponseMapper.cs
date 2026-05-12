using Lingarr.Server.Models.Batch;
using Lingarr.Server.Models.Batch.Response;
using System.Security.Cryptography;
using System.Text;

namespace Lingarr.Server.Services.Translation;

internal static class BatchTranslationResponseMapper
{
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
            if (string.IsNullOrWhiteSpace(item.SourceKey) ||
                !string.Equals(item.SourceKey, requestedSourceKey, StringComparison.Ordinal))
            {
                sourceKeyMismatchedPositions.Add(item.Position);
                continue;
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
}
