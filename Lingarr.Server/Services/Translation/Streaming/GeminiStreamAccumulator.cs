using System.Text;
using System.Text.Json;

namespace Lingarr.Server.Services.Translation.Streaming;

/// <summary>
/// Accumulates Server-Sent Events from Google Gemini's streamGenerateContent endpoint
/// into a full response content string and captures token usage.
/// Gemini returns SSE with candidates[].content.parts[].text chunks.
/// End-of-stream is signaled by an empty parts array or end of SSE.
/// </summary>
public static class GeminiStreamAccumulator
{
    /// <summary>
    /// Accumulates a streaming Gemini response into full content and token usage.
    /// The stream is fully consumed — response.EnsureSuccessStatusCode() MUST be called first.
    /// </summary>
    /// <param name="response">The HTTP response (ResponseHeadersRead must have been used).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A tuple of (accumulatedContent, promptTokenCount, candidatesTokenCount, totalTokenCount).
    /// Token values may be null if not present in the stream.
    /// </returns>
    public static async Task<(string Content, int? PromptTokens, int? CompletionTokens, int? TotalTokens)>
        AccumulateAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var reader = new SseReader(stream);
        var contentBuilder = new StringBuilder(4096);

        int? promptTokens = null;
        int? completionTokens = null;
        int? totalTokens = null;

        await foreach (var sseEvent in reader.ReadAsync(cancellationToken))
        {
            if (string.IsNullOrEmpty(sseEvent.Data))
            {
                continue;
            }

            // Gemini sometimes wraps the payload in an array (final chunk with usageMetadata)
            // or returns a single JSON object
            using var json = JsonDocument.Parse(sseEvent.Data);
            var root = json.RootElement;

            // Handle array wrapper: [{"candidates":[...],"usageMetadata":{...}}]
            if (root.ValueKind == JsonValueKind.Array)
            {
                if (root.GetArrayLength() == 0)
                {
                    continue;
                }

                root = root[0];
            }

            // Extract token usage from final chunk
            if (root.TryGetProperty("usageMetadata", out var usageMetadata) &&
                usageMetadata.ValueKind == JsonValueKind.Object)
            {
                ExtractTokenCount(usageMetadata, "promptTokenCount", out promptTokens);
                ExtractTokenCount(usageMetadata, "candidatesTokenCount", out completionTokens);
                ExtractTokenCount(usageMetadata, "totalTokenCount", out totalTokens);
            }

            // Extract text content
            if (root.TryGetProperty("candidates", out var candidates) &&
                candidates.ValueKind == JsonValueKind.Array &&
                candidates.GetArrayLength() > 0)
            {
                var candidate = candidates[0];
                if (candidate.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in parts.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var text) &&
                            text.ValueKind == JsonValueKind.String)
                        {
                            contentBuilder.Append(text.GetString());
                        }
                    }
                }
            }
        }

        return (contentBuilder.ToString(), promptTokens, completionTokens, totalTokens);
    }

    private static void ExtractTokenCount(JsonElement element, string propertyName, out int? value)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
        {
            value = prop.GetInt32();
        }
        else
        {
            value = null;
        }
    }
}
