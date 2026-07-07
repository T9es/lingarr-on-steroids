using System.Text;
using System.Text.Json;
using System.Net;
using System.Net.Http;

namespace Lingarr.Server.Tests.Data;

/// <summary>
/// Helper for creating SSE-formatted HTTP response content for test mocks.
/// The streaming providers now expect SSE-formatted data rather than raw JSON.
/// </summary>
public static class SseTestHelper
{
    /// <summary>
    /// Creates an OpenAI-compatible SSE response with the given content and optional usage metadata.
    /// Format: data: {"choices":[{"delta":{"role":"assistant","content":"..."},"index":0}]}\n\n
    ///         data: {"choices":[{"delta":{},"finish_reason":"stop","index":0}],"usage":{...}}\n\n
    ///         data: [DONE]\n\n
    /// </summary>
    public static string CreateOpenAiSseResponse(
        string content,
        int promptTokens = 12,
        int completionTokens = 8,
        int totalTokens = 20)
    {
        var sb = new StringBuilder();

        // Content chunk
        sb.Append("data: ");
        sb.Append(JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    delta = new { role = "assistant", content },
                    index = 0
                }
            }
        }));
        sb.Append("\n\n");

        // Final chunk with usage
        sb.Append("data: ");
        sb.Append(JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    delta = new { },
                    finish_reason = "stop",
                    index = 0
                }
            },
            usage = new
            {
                prompt_tokens = promptTokens,
                completion_tokens = completionTokens,
                total_tokens = totalTokens
            }
        }));
        sb.Append("\n\n");

        // Done signal
        sb.Append("data: [DONE]\n\n");

        return sb.ToString();
    }

    /// <summary>
    /// Creates a Gemini-compatible SSE response.
    /// Format: data: {"candidates":[{"content":{"parts":[{"text":"..."}]}}]}\n\n
    ///         data: [{"candidates":[{"content":{"parts":[]}}],"usageMetadata":{...}}]\n\n
    /// </summary>
    public static string CreateGeminiSseResponse(
        string text,
        int promptTokens = 12,
        int completionTokens = 8,
        int totalTokens = 20)
    {
        var sb = new StringBuilder();

        // Content chunk
        sb.Append("data: ");
        sb.Append(JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[] { new { text } },
                        role = "model"
                    }
                }
            }
        }));
        sb.Append("\n\n");

        // Final chunk with usage metadata
        sb.Append("data: ");
        sb.Append(JsonSerializer.Serialize(new[]
        {
            new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            parts = new object[0],
                            role = "model"
                        }
                    }
                },
                usageMetadata = new
                {
                    promptTokenCount = promptTokens,
                    candidatesTokenCount = completionTokens,
                    totalTokenCount = totalTokens
                }
            }
        }));
        sb.Append("\n\n");

        return sb.ToString();
    }

    /// <summary>
    /// Creates an Anthropic-compatible SSE response for tool_use batch translation.
    /// </summary>
    public static string CreateAnthropicSseResponse(
        string toolInputJson,
        int inputTokens = 12,
        int outputTokens = 8)
    {
        var sb = new StringBuilder();

        // message_start
        sb.Append("event: message_start\n");
        sb.Append("data: ");
        sb.Append(JsonSerializer.Serialize(new
        {
            type = "message_start",
            message = new
            {
                id = "msg_test_123",
                usage = new { input_tokens = inputTokens }
            }
        }));
        sb.Append("\n\n");

        // content_block_start (tool_use)
        sb.Append("event: content_block_start\n");
        sb.Append("data: ");
        sb.Append(JsonSerializer.Serialize(new
        {
            type = "content_block_start",
            index = 0,
            content_block = new
            {
                type = "tool_use",
                id = "toolu_test_123",
                name = "record_translation_batch",
                input = new { }
            }
        }));
        sb.Append("\n\n");

        // content_block_delta (input_json_delta with the actual content)
        sb.Append("event: content_block_delta\n");
        sb.Append("data: ");
        sb.Append(JsonSerializer.Serialize(new
        {
            type = "content_block_delta",
            index = 0,
            delta = new
            {
                type = "input_json_delta",
                partial_json = toolInputJson
            }
        }));
        sb.Append("\n\n");

        // content_block_stop
        sb.Append("event: content_block_stop\n");
        sb.Append("data: ");
        sb.Append(JsonSerializer.Serialize(new
        {
            type = "content_block_stop",
            index = 0
        }));
        sb.Append("\n\n");

        // message_delta (with usage)
        sb.Append("event: message_delta\n");
        sb.Append("data: ");
        sb.Append(JsonSerializer.Serialize(new
        {
            type = "message_delta",
            delta = new { stop_reason = "end_turn", stop_sequence = (string?)null },
            usage = new { output_tokens = outputTokens }
        }));
        sb.Append("\n\n");

        // message_stop
        sb.Append("event: message_stop\n");
        sb.Append("data: ");
        sb.Append(JsonSerializer.Serialize(new
        {
            type = "message_stop"
        }));
        sb.Append("\n\n");

        return sb.ToString();
    }

    /// <summary>
    /// Creates an HTTP response with SSE-formatted content for OpenAI-compatible streaming.
    /// </summary>
    public static HttpResponseMessage CreateOpenAiSseResponseMessage(
        string content,
        int promptTokens = 12,
        int completionTokens = 8,
        int totalTokens = 20)
    {
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(
                CreateOpenAiSseResponse(content, promptTokens, completionTokens, totalTokens),
                Encoding.UTF8,
                "text/event-stream")
        };
    }

    /// <summary>
    /// Creates an HTTP response with SSE-formatted content for Gemini streaming.
    /// </summary>
    public static HttpResponseMessage CreateGeminiSseResponseMessage(
        string text,
        int promptTokens = 12,
        int completionTokens = 8,
        int totalTokens = 20)
    {
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(
                CreateGeminiSseResponse(text, promptTokens, completionTokens, totalTokens),
                Encoding.UTF8,
                "text/event-stream")
        };
    }

    /// <summary>
    /// Creates an HTTP response with SSE-formatted content for Anthropic streaming.
    /// </summary>
    public static HttpResponseMessage CreateAnthropicSseResponseMessage(
        string toolInputJson,
        int inputTokens = 12,
        int outputTokens = 8)
    {
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(
                CreateAnthropicSseResponse(toolInputJson, inputTokens, outputTokens),
                Encoding.UTF8,
                "text/event-stream")
        };
    }
}
