using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Diagnostics;
using Lingarr.Core.Configuration;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Lingarr.Server.Services.Translation.Base;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Models.Batch.Response;
using Lingarr.Server.Services.Translation.Streaming;

namespace Lingarr.Server.Services.Translation;

public class OpenAiService : BaseLanguageService, ITranslationService, IBatchTranslationService
{
    protected virtual string ModelSettingKey => SettingKeys.Translation.OpenAi.Model;
    protected virtual string ApiKeySettingKey => SettingKeys.Translation.OpenAi.ApiKey;
    protected virtual string EndpointBase => "https://api.openai.com/v1/";
    protected virtual string ServiceName => "openai";

    protected readonly string _endpoint;
    protected string? _prompt;
    protected string? _model;
    protected string? _apiKey;
    protected readonly HttpClient _httpClient;
    protected bool _initialized;
    protected readonly SemaphoreSlim _initLock = new(1, 1);
    protected readonly IDashboardService? _dashboardService;
    protected readonly ITokenUsageService? _tokenUsageService;
    protected readonly IProviderCircuitBreaker? _circuitBreaker;

    // retry settings
    protected int _maxRetries;
    protected TimeSpan _retryDelay;
    protected int _retryDelayMultiplier;

    public OpenAiService(
        ISettingService settings,
        ILogger<OpenAiService> logger,
        HttpClient? httpClient = null,
        IDashboardService? dashboardService = null,
        ITokenUsageService? tokenUsageService = null,
        ITranslationPromptAugmenter? translationPromptAugmenter = null,
        IProviderCircuitBreaker? circuitBreaker = null)
        : base(settings, logger, "/app/Statics/ai_languages.json", translationPromptAugmenter)
    {
        _httpClient = httpClient ?? new HttpClient();
        _endpoint = EndpointBase;
        _dashboardService = dashboardService;
        _tokenUsageService = tokenUsageService;
        _circuitBreaker = circuitBreaker;
    }

    /// <summary>
    /// Initializes the translation service with necessary configurations and credentials.
    /// This method is thread-safe and ensures one-time initialization of service dependencies.
    /// </summary>
    /// <param name="sourceLanguage">The source language code for translation</param>
    /// <param name="targetLanguage">The target language code for translation</param>
    /// <returns>A task that represents the asynchronous initialization operation</returns>
    /// <exception cref="InvalidOperationException">Thrown when required configuration settings are missing or invalid</exception>
    protected async Task InitializeAsync(string sourceLanguage, string targetLanguage)
    {
        if (_initialized) return;

        try
        {
            await _initLock.WaitAsync();
            if (_initialized) return;

            var settings = await _settings.GetSettings([
                ModelSettingKey,
                ApiKeySettingKey,
                SettingKeys.Translation.AiPrompt,
                SettingKeys.Translation.AiContextPrompt,
                SettingKeys.Translation.AiContextPromptEnabled,
                SettingKeys.Translation.CustomAiParameters,
                SettingKeys.Translation.RequestTimeout,
                SettingKeys.Translation.MaxRetries,
                SettingKeys.Translation.RetryDelay,
                SettingKeys.Translation.RetryDelayMultiplier
            ]);

            _model = settings[ModelSettingKey];
            _apiKey = settings[ApiKeySettingKey];
            _contextPromptEnabled = settings[SettingKeys.Translation.AiContextPromptEnabled];

            if (string.IsNullOrEmpty(_model) || string.IsNullOrEmpty(_apiKey))
            {
                throw new InvalidOperationException("OpenAI API key or model is not configured.");
            }

            _replacements = new Dictionary<string, string>
            {
                ["sourceLanguage"] = GetFullLanguageName(sourceLanguage),
                ["targetLanguage"] = GetFullLanguageName(targetLanguage)
            };
            _prompt = await ApplyTranslationPromptContextAsync(
                ReplacePlaceholders(settings[SettingKeys.Translation.AiPrompt], _replacements));
            _contextPrompt = settings[SettingKeys.Translation.AiContextPrompt];
            _customParameters = PrepareCustomParameters(settings, SettingKeys.Translation.CustomAiParameters);

            var requestTimeout = int.TryParse(settings[SettingKeys.Translation.RequestTimeout],
                out var timeOut)
                ? timeOut
                : 15;
            _httpClient.Timeout = TimeSpan.FromMinutes(requestTimeout);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            _maxRetries = int.TryParse(settings[SettingKeys.Translation.MaxRetries], out var maxRetries) 
                ? maxRetries 
                : 5;
            var retryDelaySeconds = int.TryParse(settings[SettingKeys.Translation.RetryDelay], out var delaySeconds) 
                ? delaySeconds 
                : 1;
            _retryDelay = TimeSpan.FromSeconds(retryDelaySeconds);
            _retryDelayMultiplier = int.TryParse(settings[SettingKeys.Translation.RetryDelayMultiplier], out var multiplier) 
                ? multiplier 
                : 2;

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <inheritdoc />
    public override async Task<string> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        List<string>? contextLinesBefore,
        List<string>? contextLinesAfter,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(sourceLanguage, targetLanguage);

        await EnsureProviderCircuitAllowedAsync(cancellationToken);

        if (_tokenUsageService != null)
        {
            await _tokenUsageService.EnsureTokensAvailableAsync(ServiceName, cancellationToken);
        }

        text = ApplyContextIfEnabled(text, contextLinesBefore, contextLinesAfter);
        using var retry = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, retry.Token);
        
        var delay = _retryDelay;
        for (var attempt = 1; attempt <= _maxRetries; attempt++)
        {
            try
            {
                var requestUrl = await GetChatCompletionsEndpointAsync(cancellationToken);
                var requestBody = new Dictionary<string, object>
                {
                    ["model"] = _model!,
                    ["messages"] = new[]
                    {
                        new Dictionary<string, string>
                        {
                            ["role"] = "system",
                            ["content"] = _prompt!
                        },
                        new Dictionary<string, string>
                        {
                            ["role"] = "user",
                            ["content"] = text
                        }
                    }
                };

                requestBody = AddCustomParameters(requestBody);
                await EnrichChatCompletionRequestAsync(requestBody, cancellationToken);
                var requestContent = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json");

                var stopwatch = Stopwatch.StartNew();
                var response = await _httpClient.PostAsync(requestUrl, requestContent, linked.Token);
                stopwatch.Stop();
                
                if (!response.IsSuccessStatusCode)
                {
                    if (_dashboardService != null && response.StatusCode != HttpStatusCode.TooManyRequests)
                    {
                        await _dashboardService.LogApiUsage(ServiceName, null, stopwatch.ElapsedMilliseconds, false, $"Status: {response.StatusCode}");
                    }

                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        var responseBody = await response.Content.ReadAsStringAsync(linked.Token);
                        _logger.LogWarning("429 Rate Limit Exceeded. Provider Message: {Content}", responseBody);
                        throw new HttpRequestException("Rate limit exceeded", null, HttpStatusCode.TooManyRequests);
                    }

                    if ((int)response.StatusCode >= 500 && (int)response.StatusCode < 600)
                    {
                        var responseBody = await response.Content.ReadAsStringAsync(linked.Token);
                        _logger.LogWarning("{StatusCode} Server Error. Provider Message: {Content}", response.StatusCode, responseBody);
                        throw new HttpRequestException("Provider server error", null, HttpStatusCode.ServiceUnavailable);
                    }

                    _logger.LogError("Response Status Code: {StatusCode}", response.StatusCode);
                    _logger.LogError("Response Content: {ResponseContent}",
                        await response.Content.ReadAsStringAsync(cancellationToken: linked.Token));
                    throw new TranslationException(
                        $"Translation using OpenAI failed with status code {response.StatusCode}.");
                }

                var completionResponse =
                    await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(linked.Token);
                if (completionResponse?.Choices == null || completionResponse.Choices.Count == 0)
                {
                    throw new TranslationException("No completion choices returned from OpenAI");
                }

// Log API usage
                if (_dashboardService != null)
                {
                    await _dashboardService.LogApiUsage(
                        ServiceName,
                        completionResponse.Usage?.TotalTokens,
                        stopwatch.ElapsedMilliseconds,
                        success: true,
                        promptTokens: completionResponse.Usage?.PromptTokens,
                        completionTokens: completionResponse.Usage?.CompletionTokens);
                }

                _circuitBreaker?.RecordSuccess(ServiceName);

                return completionResponse.Choices[0].Message.Content;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                await RecordProviderFailureAsync(ex, cancellationToken);

                if (attempt == _maxRetries)
                {
                    _logger.LogError(ex, "Too many requests. Max retries exhausted for text: {Text}", text);
                    throw new TranslationException("Too many requests. Retry limit reached.", ex);
                }

                await Task.Delay(delay, linked.Token).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * _retryDelayMultiplier);

                _logger.LogWarning(
                    "OpenAI rate limit hit. Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
                    delay, attempt, _maxRetries);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable || ex.StatusCode == HttpStatusCode.GatewayTimeout || ex.StatusCode == HttpStatusCode.BadGateway)
            {
                await RecordProviderFailureAsync(ex, cancellationToken);

                if (attempt == _maxRetries)
                {
                    _logger.LogError(ex, "OpenAI server error, it might be down. Max retries exhausted for text: {Text}", text);
                    throw new TranslationException("OpenAI is temporarily unavailable, usually due to high load or maintenance. Retry limit reached.", ex);
                }

                await Task.Delay(delay, linked.Token).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * _retryDelayMultiplier);

                _logger.LogWarning(
                    "OpenAI service unavailable. Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
                    delay, attempt, _maxRetries);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is TaskCanceledException || (ex is HttpRequestException && ex.InnerException is IOException))
            {
                if (attempt == _maxRetries)
                {
                    _logger.LogError(ex, "Network error during translation. Max retries exhausted for text: {Text}", text);
                    throw new TranslationException("Network error occurred during translation.", ex);
                }

                await Task.Delay(delay, linked.Token).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * _retryDelayMultiplier);
                
                _logger.LogWarning(ex, "Network error (Transient). Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})", delay, attempt, _maxRetries);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during OpenAI translation");
                throw new TranslationException("Failed to translate using OpenAI", ex);
            }
        }

        throw new TranslationException("Translation failed after maximum retry attempts.");
    }

    /// <summary>
    /// Translates a batch of subtitles in a single API call using OpenAI's structured output
    /// </summary>
    /// <param name="subtitleBatch">List of subtitles with position and content</param>
    /// <param name="sourceLanguage">Source language code</param>
    /// <param name="targetLanguage">Target language code</param>
    /// <param name="preContext">Optional context lines before the batch</param>
    /// <param name="postContext">Optional context lines after the batch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary mapping position to translated content</returns>
    public virtual async Task<Dictionary<int, string>> TranslateBatchAsync(
        List<BatchSubtitleItem> subtitleBatch,
        string sourceLanguage,
        string targetLanguage,
        List<string>? preContext,
        List<string>? postContext,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(sourceLanguage, targetLanguage);

        await EnsureProviderCircuitAllowedAsync(cancellationToken);

        using var retry = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, retry.Token);
        
        var delay = _retryDelay;
        for (var attempt = 1; attempt <= _maxRetries; attempt++)
        {
            try
            {
                var result = await TranslateBatchWithOpenAiApi(subtitleBatch, preContext, postContext, linked.Token);
                _circuitBreaker?.RecordSuccess(ServiceName);
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
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable || ex.StatusCode == HttpStatusCode.GatewayTimeout || ex.StatusCode == HttpStatusCode.BadGateway)
            {
                await RecordProviderFailureAsync(ex, cancellationToken);

                if (attempt == _maxRetries)
                {
                    _logger.LogError(ex, "Service unavailable. Max retries exhausted for batch translation");
                    throw new TranslationException("OpenAI is temporarily unavailable. Retry limit reached.", ex);
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
                // For truly unexpected errors (parsing, logic), we might not want to retry, or maybe we do?
                // Current behavior: Abort.
                // Let's stick to aborting for non-network errors to avoid wasting API credits on bad requests.
                throw new TranslationException("Unexpected error occurred during batch translation.", ex);
            }
        }

        throw new TranslationException("Batch translation failed after maximum retry attempts.");
    }

    private async Task<Dictionary<int, string>> TranslateBatchWithOpenAiApi(
        List<BatchSubtitleItem> subtitleBatch,
        List<string>? preContext,
        List<string>? postContext,
        CancellationToken cancellationToken)
    {
        var requestUrl = await GetChatCompletionsEndpointAsync(cancellationToken);
        var responseFormat = new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "batch_translation_response",
                schema = new
                {
                    type = "object",
                    properties = new
                    {
                        translations = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    position = new
                                    {
                                        type = "integer"
                                    },
                                    sourceKey = new
                                    {
                                        type = "string"
                                    },
                                    line = new
                                    {
                                        type = "string"
                                    }
                                },
                                required = new[] { "position", "sourceKey", "line" },
                                additionalProperties = false
                            }
                        }
                    },
                    required = new[] { "translations" },
                    additionalProperties = false
                }
            }
        };

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
                    ["content"] = _prompt!
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
                // Include the response body in the exception so handlers can extract reset timestamp
                throw new TranslationException($"Batch translation using OpenAI API failed. Status: PaymentRequired. Response: {responseBody}");
            }

            if ((int)response.StatusCode >= 500 && (int)response.StatusCode < 600)
            {
                _logger.LogWarning("{StatusCode} Server Error (Batch). Provider Message: {Content}", response.StatusCode, responseBody);
                throw new HttpRequestException("Provider server error", null, HttpStatusCode.ServiceUnavailable);
            }
            
            _logger.LogError(
                "Batch translation API failed. Status: {StatusCode}, BatchSize: {BatchSize}, Endpoint: {Endpoint}",
                response.StatusCode, subtitleBatch.Count, requestUrl);
            _logger.LogError("API Response Body: {ResponseContent}", responseBody);
            
            // Log a sample of the request for debugging (first 3 items)
            var sampleItems = subtitleBatch.Take(3).Select(i => $"[{i.Position}] {i.Line.Substring(0, Math.Min(50, i.Line.Length))}...");
            _logger.LogDebug("Request sample (first 3 items): {Sample}", string.Join("; ", sampleItems));
            
            throw new TranslationException($"Batch translation using OpenAI API failed. Status: {response.StatusCode}");
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
            var responseWrapper = JsonSerializer.Deserialize<JsonElement>(translatedJson);
            if (!responseWrapper.TryGetProperty("translations", out var translationsElement))
            {
                throw new TranslationException("Response does not contain 'translations' property");
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

            return BatchTranslationResponseMapper.MapAlignedTranslations(
                subtitleBatch,
                translatedItems,
                _logger,
                ServiceName);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse translated JSON. BatchSize: {BatchSize}, Response: {Json}", 
                subtitleBatch.Count, translatedJson?.Substring(0, Math.Min(500, translatedJson?.Length ?? 0)));
            throw new TranslationException("Failed to parse translated subtitles", ex);
        }
    }

    /// <inheritdoc />
    public override async Task<ModelsResponse> GetModels()
    {
        var apiKey = await _settings.GetSetting(ApiKeySettingKey);

        if (string.IsNullOrEmpty(apiKey))
        {
            return new ModelsResponse
            {
                Message = "OpenAI API key is not configured."
            };
        }

        try
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var requestUrl = $"{_endpoint}models";
            var response = await client.GetAsync(requestUrl);

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
                    Message = "No models data returned from OpenAI API."
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
            _logger.LogError(ex, "HTTP error fetching models from OpenAI API");
            return new ModelsResponse
            {
                Message = $"HTTP error fetching models from OpenAI API: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching models from OpenAI API");
            return new ModelsResponse
            {
                Message = $"Error fetching models from OpenAI API: {ex.Message}"
            };
        }
    }

    protected virtual Task<string> GetChatCompletionsEndpointAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult($"{_endpoint}chat/completions");
    }

    protected virtual Task EnrichChatCompletionRequestAsync(
        Dictionary<string, object> requestBody,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected Task EnsureProviderCircuitAllowedAsync(CancellationToken cancellationToken)
    {
        return _circuitBreaker?.EnsureAllowedAsync(ServiceName, cancellationToken) ?? Task.CompletedTask;
    }

    protected async Task RecordProviderFailureAsync(Exception exception, CancellationToken cancellationToken)
    {
        if (_circuitBreaker == null)
        {
            return;
        }

        _circuitBreaker.RecordFailure(ServiceName, exception);
        await _circuitBreaker.EnsureAllowedAsync(ServiceName, cancellationToken);
    }
}
