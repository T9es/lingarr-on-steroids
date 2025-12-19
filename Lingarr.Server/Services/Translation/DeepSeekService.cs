using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.IO;
using System.Text;
using System.Text.Json;
using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Models.Batch.Response;
using Lingarr.Server.Exceptions;

namespace Lingarr.Server.Services.Translation;

/// <summary>
/// DeepSeek translation service that leverages the OpenAI-compatible API.
/// Extends OpenAiService to inherit batch translation support with deferred repair.
/// </summary>
public class DeepSeekService : OpenAiService
{
    protected override string ModelSettingKey => SettingKeys.Translation.DeepSeek.Model;
    protected override string ApiKeySettingKey => SettingKeys.Translation.DeepSeek.ApiKey;
    protected override string EndpointBase => "https://api.deepseek.com/";

    public DeepSeekService(
        ISettingService settings,
        ILogger<DeepSeekService> logger,
        IHttpClientFactory httpClientFactory)
        : base(settings, logger, httpClientFactory.CreateClient(nameof(DeepSeekService)))
    {
    }

    /// <inheritdoc />
    public override async Task<ModelsResponse> GetModels()
    {
        var apiKey = await _settings.GetSetting(ApiKeySettingKey);

        if (string.IsNullOrEmpty(apiKey))
        {
            return new ModelsResponse
            {
                Message = "DeepSeek API key is not configured."
            };
        }

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(EndpointBase) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            var response = await client.GetAsync("models");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to fetch models. Status: {StatusCode}", response.StatusCode);
                return new ModelsResponse
                {
                    Message = $"Failed to fetch models. Status: {response.StatusCode}"
                };
            }

            var modelsResponse = await response.Content.ReadFromJsonAsync<ModelsListResponse>();

            if (modelsResponse?.Data == null)
            {
                return new ModelsResponse
                {
                    Message = "No models data returned from DeepSeek API."
                };
            }

            var labelValues = modelsResponse.Data
                .Select(model => new LabelValue
                {
                    Label = model.Id,
                    Value = model.Id
                })
                .ToList();

            return new ModelsResponse
            {
                Options = labelValues
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching models from DeepSeek API");
            return new ModelsResponse
            {
                Message = $"HTTP error fetching models from DeepSeek API: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching models from DeepSeek API");
            return new ModelsResponse
            {
                Message = $"Error fetching models from DeepSeek API: {ex.Message}"
            };
        }
    }

    public override async Task<Dictionary<int, string>> TranslateBatchAsync(
        List<BatchSubtitleItem> subtitleBatch,
        string sourceLanguage,
        string targetLanguage,
        List<string>? preContext,
        List<string>? postContext,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(sourceLanguage, targetLanguage);

        using var retry = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, retry.Token);
        
        var delay = _retryDelay;
        for (var attempt = 1; attempt <= _maxRetries; attempt++)
        {
            try
            {
                return await TranslateBatchWithDeepSeekApi(subtitleBatch, preContext, postContext, linked.Token);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                if (attempt == _maxRetries)
                {
                    _logger.LogError(ex, "Too many requests. Max retries exhausted for batch translation");
                    throw new TranslationException("Too many requests. Retry limit reached.", ex);
                }

                _logger.LogWarning(
                    "429 Too Many Requests. Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
                    delay, attempt, _maxRetries);

                await Task.Delay(delay, linked.Token).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * _retryDelayMultiplier);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable || ex.StatusCode == HttpStatusCode.GatewayTimeout || ex.StatusCode == HttpStatusCode.BadGateway)
            {
                if (attempt == _maxRetries)
                {
                    _logger.LogError(ex, "Service unavailable. Max retries exhausted for batch translation");
                    throw new TranslationException("DeepSeek is temporarily unavailable. Retry limit reached.", ex);
                }

                _logger.LogWarning(
                    "{StatusCode} Service Unavailable. Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
                    ex.StatusCode, delay, attempt, _maxRetries);

                await Task.Delay(delay, linked.Token).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * _retryDelayMultiplier);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                 throw;
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is TaskCanceledException || (ex is HttpRequestException && ex.InnerException is IOException))
            {
                if (attempt == _maxRetries)
                {
                    _logger.LogError(ex, "Network error during batch translation. Max retries exhausted");
                    throw new TranslationException("Network error occurred during batch translation.", ex);
                }

                _logger.LogWarning(ex,
                    "Network error (Transient). Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
                    delay, attempt, _maxRetries);

                await Task.Delay(delay, linked.Token).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * _retryDelayMultiplier);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during batch translation attempt {Attempt}", attempt);
                throw new TranslationException("Unexpected error occurred during batch translation.", ex);
            }
        }

        throw new TranslationException("Batch translation failed after maximum retry attempts.");
    }

    private async Task<Dictionary<int, string>> TranslateBatchWithDeepSeekApi(
        List<BatchSubtitleItem> subtitleBatch,
        List<string>? preContext,
        List<string>? postContext,
        CancellationToken cancellationToken)
    {
        var requestUrl = $"{_endpoint}chat/completions";
        
        // Use json_object response format
        var responseFormat = new { type = "json_object" };

        // Construct a system prompt that enforces the JSON schema
        var systemPrompt = _prompt + "\n\n" +
                           "IMPORTANT: You must output a valid JSON object matching this schema:\n" +
                           "{\n" +
                           "  \"translations\": [\n" +
                           "    { \"position\": 1, \"line\": \"Translated text\" }\n" +
                           "  ]\n" +
                           "}\n" +
                           "The 'position' field must match the input position. The 'line' field contains the translated text.";

        // Build user content with optional batch context wrapper
        var userContent = BuildBatchUserContent(subtitleBatch, preContext, postContext);

        var requestBody = new Dictionary<string, object>
        {
            ["model"] = _model!,
            ["messages"] = new[]
            {
                new Dictionary<string, string>
                {
                    ["role"] = "system",
                    ["content"] = systemPrompt
                },
                new Dictionary<string, string>
                {
                    ["role"] = "user",
                    ["content"] = userContent
                }
            },
            ["response_format"] = responseFormat
        };

        // Add custom parameters but exclude response_format to avoid conflicts
        if (_customParameters is { Count: > 0 })
        {
            foreach (var param in _customParameters)
            {
                if (param.Key != "response_format")
                {
                    requestBody[param.Key] = param.Value;
                }
            }
        }

        var requestContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync(requestUrl, requestContent, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("429 Rate Limit Exceeded (Batch). Provider Message: {Content}", responseBody);
                throw new HttpRequestException("Rate limit exceeded", null, statusCode: HttpStatusCode.TooManyRequests);
            }

            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                throw new HttpRequestException("DeepSeek temporary unavailable", null, statusCode: HttpStatusCode.ServiceUnavailable);
            }
            
            _logger.LogError(
                "Batch translation API failed. Status: {StatusCode}, BatchSize: {BatchSize}, Endpoint: {Endpoint}",
                response.StatusCode, subtitleBatch.Count, requestUrl);
            _logger.LogError("API Response Body: {ResponseContent}", responseBody);
            
            throw new TranslationException($"Batch translation using DeepSeek API failed. Status: {response.StatusCode}");
        }

        var completionResponse = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken);
        if (completionResponse?.Choices == null || completionResponse.Choices.Count == 0)
        {
            throw new TranslationException("No completion choices returned from DeepSeek");
        }
        
        var translatedJson = completionResponse.Choices[0].Message.Content;
        try
        {
            // DeepSeek might wrap it in markdown code block ```json ... ```
            if (translatedJson.StartsWith("```json"))
            {
                translatedJson = translatedJson.Replace("```json", "").Replace("```", "").Trim();
            }
            else if (translatedJson.StartsWith("```"))
            {
                translatedJson = translatedJson.Replace("```", "").Trim();
            }

            var responseWrapper = JsonSerializer.Deserialize<JsonElement>(translatedJson);
            if (!responseWrapper.TryGetProperty("translations", out var translationsElement))
            {
                // Fallback: maybe it returned an array directly?
                if (responseWrapper.ValueKind == JsonValueKind.Array)
                {
                    translationsElement = responseWrapper;
                }
                else
                {
                    throw new TranslationException("Response does not contain 'translations' property");
                }
            }

            var translatedItems =
                JsonSerializer.Deserialize<List<StructuredBatchResponse>>(translationsElement.GetRawText());
            if (translatedItems == null)
            {
                throw new TranslationException("Failed to deserialize translated subtitles");
            }

            // Log success with counts for diagnostics
            _logger.LogDebug(
                "Batch translation successful. Requested: {RequestedCount}, Received: {ReceivedCount}",
                subtitleBatch.Count, translatedItems.Count);

            return translatedItems
                .GroupBy(item => item.Position)
                .ToDictionary(group => group.Key, group => group.First().Line);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse translated JSON. BatchSize: {BatchSize}, Response: {Json}", 
                subtitleBatch.Count, translatedJson?.Substring(0, Math.Min(500, translatedJson?.Length ?? 0)));
            throw new TranslationException("Failed to parse translated subtitles", ex);
        }
    }
}