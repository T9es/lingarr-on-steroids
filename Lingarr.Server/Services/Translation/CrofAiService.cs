using System.Net.Http.Headers;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Net.Sockets;
using System.Text.Json;
using System.IO;
using Lingarr.Core.Configuration;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Models.Batch.Response;
using Lingarr.Server.Services.Translation.Streaming;

namespace Lingarr.Server.Services.Translation;

public class CrofAiService : OpenAiService
{
    private readonly ICrofAiUsageService _usageService;

    protected override string ModelSettingKey => SettingKeys.Translation.CrofAi.Model;
    protected override string ApiKeySettingKey => SettingKeys.Translation.CrofAi.ApiKey;
    protected override string EndpointBase => "https://crof.ai/v1/";
    protected override string ServiceName => "crofai";

    public CrofAiService(
        ISettingService settings,
        ILogger<CrofAiService> logger,
        ICrofAiUsageService usageService,
        IHttpClientFactory httpClientFactory,
        IDashboardService? dashboardService = null,
        ITokenUsageService? tokenUsageService = null,
        ITranslationPromptAugmenter? translationPromptAugmenter = null,
        IProviderCircuitBreaker? circuitBreaker = null)
        : base(settings, logger, httpClientFactory.CreateClient(nameof(CrofAiService)), dashboardService, tokenUsageService, translationPromptAugmenter, circuitBreaker)
    {
        _usageService = usageService;
    }

    public override async Task<string> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        List<string>? contextLinesBefore,
        List<string>? contextLinesAfter,
        CancellationToken cancellationToken)
    {
        await _usageService.EnsureRequestAllowedAsync(cancellationToken);

        try
        {
            var result = await base.TranslateAsync(
                text,
                sourceLanguage,
                targetLanguage,
                contextLinesBefore,
                contextLinesAfter,
                cancellationToken);

            await _usageService.RecordRequestAsync(cancellationToken);
            return result;
        }
        catch (TranslationException)
        {
            throw;
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
        await _usageService.EnsureRequestAllowedAsync(cancellationToken);
        await InitializeAsync(sourceLanguage, targetLanguage);

        using var retry = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, retry.Token);

        var delay = _retryDelay;
        for (var attempt = 1; attempt <= _maxRetries; attempt++)
        {
            try
            {
                var result = await TranslateBatchWithCrofAiApi(
                    subtitleBatch, preContext, postContext, linked.Token);
                _circuitBreaker?.RecordSuccess(ServiceName);
                await _usageService.RecordRequestAsync(cancellationToken);
                return result;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                await RecordProviderFailureAsync(ex, cancellationToken);

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
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable ||
                ex.StatusCode == HttpStatusCode.GatewayTimeout || ex.StatusCode == HttpStatusCode.BadGateway)
            {
                await RecordProviderFailureAsync(ex, cancellationToken);

                if (attempt == _maxRetries)
                {
                    _logger.LogError(ex, "Service unavailable. Max retries exhausted for batch translation");
                    throw new TranslationException("CrofAI is temporarily unavailable. Retry limit reached.", ex);
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
            catch (Exception ex) when (ex is IOException || ex is SocketException ||
                ex is TaskCanceledException || (ex is HttpRequestException && ex.InnerException is IOException))
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
            catch (TranslationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during batch translation attempt {Attempt}", attempt);
                throw new TranslationException("Unexpected error occurred during batch translation.", ex);
            }
        }

        throw new TranslationException("Batch translation failed after maximum retry attempts.");
    }

    public override async Task<ModelsResponse> GetModels()
    {
        var apiKey = await _settings.GetSetting(ApiKeySettingKey);

        if (string.IsNullOrEmpty(apiKey))
        {
            return new ModelsResponse
            {
                Message = "CrofAI API key is not configured."
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
                    Message = "No models data returned from CrofAI API."
                };
            }

            var labelValues = modelsResponse.Data
                .Select(model => new LabelValue
                {
                    Label = FormatModelLabel(model),
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
            _logger.LogError(ex, "HTTP error fetching models from CrofAI API");
            return new ModelsResponse
            {
                Message = $"HTTP error fetching models from CrofAI API: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching models from CrofAI API");
            return new ModelsResponse
            {
                Message = $"Error fetching models from CrofAI API: {ex.Message}"
            };
        }
    }

    private static string FormatModelLabel(ModelData model)
    {
        var modelName = !string.IsNullOrEmpty(model.Name) ? model.Name : model.Id;

        if (model.Pricing is { Prompt: not null, Completion: not null })
        {
            var promptPrice = model.Pricing.Prompt.Value;
            var completionPrice = model.Pricing.Completion.Value;
            return $"{modelName} · ${promptPrice:F4}/${completionPrice:F4} per MTok";
        }

        return modelName;
    }
    private async Task<Dictionary<int, string>> TranslateBatchWithCrofAiApi(
        List<BatchSubtitleItem> subtitleBatch,
        List<string>? preContext,
        List<string>? postContext,
        CancellationToken cancellationToken)
    {
        var requestUrl = await GetChatCompletionsEndpointAsync(cancellationToken);

        // Use json_object instead of json_schema (fixes Flash 500 error)
        var responseFormat = new { type = "json_object" };

        // Inject exact JSON schema into system prompt
        var systemPrompt = _prompt + "\n\n" +
                           "IMPORTANT: You must output a valid JSON object matching this schema:\n" +
                           "{\n" +
                           "  \"translations\": [\n" +
                           "    { \"position\": 1, \"sourceKey\": \"abc123def456\", \"line\": \"Translated text\" }\n" +
                           "  ]\n" +
                           "}\n" +
                           "The 'position' and 'sourceKey' fields must match the input item. " +
                           "The 'line' field contains the translated text. " +
                           "If you cannot translate a line, output it exactly as-is in the original language. Do NOT skip any position.";

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
        // Add streaming params — these MUST NOT be overridden by custom parameters
        requestBody["stream"] = true;
        requestBody["stream_options"] = new Dictionary<string, object>
        {
            ["include_usage"] = true
        };

        await EnrichChatCompletionRequestAsync(requestBody, cancellationToken);

        var requestContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var stopwatch = Stopwatch.StartNew();
        var request = new HttpRequestMessage(HttpMethod.Post, requestUrl) { Content = requestContent };
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("429 Rate Limit Exceeded (Batch). Provider Message: {Content}", responseBody);
                throw new HttpRequestException("Rate limit exceeded", null, HttpStatusCode.TooManyRequests);
            }

            if (response.StatusCode == HttpStatusCode.PaymentRequired)
            {
                _logger.LogWarning("402 Payment Required (Batch). Provider Message: {Content}", responseBody);
                throw new TranslationException(
                    $"Batch translation using CrofAI API failed. Status: PaymentRequired. Response: {responseBody}");
            }

            if ((int)response.StatusCode >= 500 && (int)response.StatusCode < 600)
            {
                _logger.LogWarning("{StatusCode} Server Error (Batch). Provider Message: {Content}",
                    response.StatusCode, responseBody);
                throw new HttpRequestException("Provider server error", null, HttpStatusCode.ServiceUnavailable);
            }

            _logger.LogError(
                "Batch translation API failed. Status: {StatusCode}, BatchSize: {BatchSize}, Endpoint: {Endpoint}",
                response.StatusCode, subtitleBatch.Count, requestUrl);
            _logger.LogError("API Response Body: {ResponseContent}", responseBody);

            throw new TranslationException($"Batch translation using CrofAI API failed. Status: {response.StatusCode}");
        }

        var (translatedJson, promptTokens, completionTokens, totalTokens) =
            await OpenAiStreamAccumulator.AccumulateAsync(response, cancellationToken);

        if (string.IsNullOrEmpty(translatedJson))
        {
            throw new TranslationException("Empty response received from streaming API");
        }

        // Log API usage for batch
        if (_dashboardService != null)
        {
            await _dashboardService.LogApiUsage(
                ServiceName,
                totalTokens,
                stopwatch.ElapsedMilliseconds,
                success: true,
                promptTokens: promptTokens,
                completionTokens: completionTokens);
        }

        try
        {
            // Strip markdown code fences (some models still wrap JSON in ```json```)
            if (translatedJson.StartsWith("```json"))
            {
                translatedJson = translatedJson.Replace("```json", "").Replace("```", "").Trim();
            }
            else if (translatedJson.StartsWith("```"))
            {
                translatedJson = translatedJson.Replace("```", "").Trim();
            }

            var responseWrapper = JsonSerializer.Deserialize<JsonElement>(translatedJson);
            JsonElement translationsElement;
            if (responseWrapper.ValueKind == JsonValueKind.Array)
            {
                // Model returned a bare array directly
                translationsElement = responseWrapper;
            }
            else if (responseWrapper.ValueKind == JsonValueKind.Object &&
                responseWrapper.TryGetProperty("translations", out var prop))
            {
                translationsElement = prop;
            }
            else
            {
                throw new TranslationException("Response does not contain 'translations' property");
            }

            var translatedItems =
                JsonSerializer.Deserialize<List<StructuredBatchResponse>>(translationsElement.GetRawText());
            if (translatedItems == null)
            {
                throw new TranslationException("Failed to deserialize translated subtitles");
            }

            _logger.LogDebug(
                "Batch translation successful. Requested: {RequestedCount}, Received: {ReceivedCount}",
                subtitleBatch.Count, translatedItems.Count);

            return BatchTranslationResponseMapper.MapAlignedTranslations(
                subtitleBatch,
                translatedItems,
                _logger,
                ServiceName);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse translated JSON. BatchSize: {BatchSize}, Response: {Json}",
                subtitleBatch.Count, translatedJson.Substring(0, Math.Min(500, translatedJson.Length)));
            throw new TranslationException("Failed to parse translated subtitles", ex);
        }
    }
}
