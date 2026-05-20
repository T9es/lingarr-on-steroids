using System.Text;
using System.Text.Json;

namespace Lingarr.Server.Services.Translation.Streaming;

/// <summary>
/// Accumulates Server-Sent Events from Anthropic's streaming messages API
/// into a full response content string and captures token usage.
/// Handles content_block_delta (text_delta and input_json_delta) and message_delta events.
/// </summary>
public static class AnthropicStreamAccumulator
{
    /// <summary>
    /// Accumulates a streaming Anthropic messages response into full content and token usage.
    /// The stream is fully consumed — response.EnsureSuccessStatusCode() MUST be called first.
    /// </summary>
    /// <param name="response">The HTTP response (ResponseHeadersRead must have been used).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A tuple of (accumulatedContent, inputTokens, outputTokens, totalTokens).
    /// Token values may be null if the provider did not include usage information.
    /// </returns>
    public static async Task<(string Content, int? InputTokens, int? OutputTokens, int? TotalTokens)>
        AccumulateAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var reader = new SseReader(stream);
        var contentBuilder = new StringBuilder(4096);

        int? inputTokens = null;
        int? outputTokens = null;

        await foreach (var sseEvent in reader.ReadAsync(cancellationToken))
        {
            if (string.IsNullOrEmpty(sseEvent.Data))
            {
                continue;
            }

            using var json = JsonDocument.Parse(sseEvent.Data);
            var root = json.RootElement;

            // Get event type from the SSE event field, or from the JSON body for Anthropic's inline type
            var eventType = sseEvent.EventType;
            if (eventType == null && root.TryGetProperty("type", out var typeProp))
            {
                eventType = typeProp.GetString();
            }

            switch (eventType)
            {
                case "content_block_start":
                    // For text content blocks, capture initial text
                    if (root.TryGetProperty("content_block", out var contentBlock) &&
                        contentBlock.TryGetProperty("type", out var blockType) &&
                        blockType.GetString() == "text" &&
                        contentBlock.TryGetProperty("text", out var startText))
                    {
                        contentBuilder.Append(startText.GetString());
                    }
                    break;

                case "content_block_delta":
                    if (root.TryGetProperty("delta", out var delta))
                    {
                        var deltaType = delta.TryGetProperty("type", out var dt) ? dt.GetString() : null;

                        switch (deltaType)
                        {
                            case "text_delta":
                                if (delta.TryGetProperty("text", out var textDelta))
                                {
                                    contentBuilder.Append(textDelta.GetString());
                                }
                                break;

                            case "input_json_delta":
                                // Tool_use input accumulates as partial JSON
                                if (delta.TryGetProperty("partial_json", out var partialJson))
                                {
                                    contentBuilder.Append(partialJson.GetString());
                                }
                                break;
                        }
                    }
                    break;

                case "message_start":
                    // Capture input tokens from the initial message
                    if (root.TryGetProperty("message", out var message))
                    {
                        ExtractUsage(message, "input_tokens", out inputTokens);
                    }
                    break;

                case "message_delta":
                    // Capture output tokens
                    if (root.TryGetProperty("usage", out var usage))
                    {
                        ExtractUsage(usage, "output_tokens", out outputTokens);
                    }
                    break;

                case "message_stop":
                    // End of stream signal
                    break;
            }
        }

        var total = (inputTokens.HasValue || outputTokens.HasValue)
            ? (inputTokens ?? 0) + (outputTokens ?? 0)
            : (int?)null;

        return (contentBuilder.ToString(), inputTokens, outputTokens, total);
    }

    private static void ExtractUsage(JsonElement element, string propertyName, out int? value)
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
