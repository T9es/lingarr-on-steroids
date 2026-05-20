using System.Text;
using System.Text.Json;

namespace Lingarr.Server.Services.Translation.Streaming;

/// <summary>
/// Accumulates Server-Sent Events from OpenAI-compatible streaming chat completions
/// into a single content string and captures token usage.
/// Handles both OpenAI format (data: {...}\n\n data: [DONE]) and any compatible provider.
/// Supports stream_options: {"include_usage": true} for token usage in the final chunk.
/// </summary>
public static class OpenAiStreamAccumulator
{
    /// <summary>
    /// Accumulates a streaming OpenAI chat completion response into full content and token usage.
    /// The stream is fully consumed — response.EnsureSuccessStatusCode() MUST be called first.
    /// </summary>
    /// <param name="response">The HTTP response (ResponseHeadersRead must have been used).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A tuple of (accumulatedContent, promptTokens, completionTokens, totalTokens).
    /// Token values may be null if the provider did not include usage information.
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

            // Terminal signal for OpenAI
            if (sseEvent.Data == "[DONE]")
            {
                break;
            }

            using var json = JsonDocument.Parse(sseEvent.Data);
            var root = json.RootElement;

            // Extract token usage from final chunk (stream_options: include_usage)
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("prompt_tokens", out var pt) && pt.ValueKind == JsonValueKind.Number)
                    promptTokens = pt.GetInt32();
                if (usage.TryGetProperty("completion_tokens", out var ct) && ct.ValueKind == JsonValueKind.Number)
                    completionTokens = ct.GetInt32();
                if (usage.TryGetProperty("total_tokens", out var tt) && tt.ValueKind == JsonValueKind.Number)
                    totalTokens = tt.GetInt32();
            }

            // Extract content delta
            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("delta", out var delta) &&
                    delta.ValueKind == JsonValueKind.Object &&
                    delta.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.String)
                {
                    contentBuilder.Append(content.GetString());
                }
            }
        }

        return (contentBuilder.ToString(), promptTokens, completionTokens, totalTokens);
    }
}
