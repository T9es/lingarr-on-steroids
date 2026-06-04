using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Lingarr.Core.Configuration;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Models.Batch.Response;
using Lingarr.Server.Models.Integrations.Translation;
using Lingarr.Server.Services.Translation.Base;
using Lingarr.Server.Services.Translation.Streaming;

namespace Lingarr.Server.Services.Translation;

public class LocalAiService : BaseLanguageService, ITranslationService, IBatchTranslationService
{
    private readonly HttpClient _httpClient;
    private readonly IDashboardService? _dashboardService;
    private readonly ITokenUsageService? _tokenUsageService;
    private const string ServiceName = "localai";
    private string? _model;
    private string? _endpoint;
    private string? _prompt;
    private new Dictionary<string, string> _replacements = new();
    private bool _isChatEndpoint;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    // retry settings
    private int _maxRetries;
    private TimeSpan _retryDelay;
    private int _retryDelayMultiplier;

    public LocalAiService(
        ISettingService settings,
        HttpClient httpClient,
        ILogger<LocalAiService> logger,
        IDashboardService? dashboardService = null,
        ITokenUsageService? tokenUsageService = null,
        ITranslationPromptAugmenter? translationPromptAugmenter = null)
        : base(settings, logger, "/app/Statics/ai_languages.json", translationPromptAugmenter)
    {
        _httpClient = httpClient;
        _dashboardService = dashboardService;
        _tokenUsageService = tokenUsageService;
    }

    /// <summary>
    /// Initializes the translation service with necessary configurations and credentials.
    /// This method is thread-safe and ensures one-time initialization of service dependencies.
    /// </summary>
    /// <param name="sourceLanguage">The source language code for translation</param>
    /// <param name="targetLanguage">The target language code for translation</param>
    /// <returns>A task that represents the asynchronous initialization operation</returns>
    /// <exception cref="InvalidOperationException">Thrown when required configuration settings are missing or invalid</exception>
    private async Task InitializeAsync(string sourceLanguage, string targetLanguage)
    {
        if (_initialized) return;

        try
        {
            await _initLock.WaitAsync();
            if (_initialized) return;

            var settings = await _settings.GetSettings([
                SettingKeys.Translation.LocalAi.Model,
                SettingKeys.Translation.LocalAi.Endpoint,
                SettingKeys.Translation.LocalAi.ApiKey,
                SettingKeys.Translation.AiPrompt,
                SettingKeys.Translation.AiContextPrompt,
                SettingKeys.Translation.AiContextPromptEnabled,
                SettingKeys.Translation.CustomAiParameters,
                SettingKeys.Translation.RequestTimeout,
                SettingKeys.Translation.MaxRetries,
                SettingKeys.Translation.RetryDelay,
                SettingKeys.Translation.RetryDelayMultiplier
            ]);
            _model = settings[SettingKeys.Translation.LocalAi.Model];
            _endpoint = settings[SettingKeys.Translation.LocalAi.Endpoint];
            _contextPromptEnabled = settings[SettingKeys.Translation.AiContextPromptEnabled];

            if (string.IsNullOrEmpty(_model) || string.IsNullOrEmpty(_endpoint))
            {
                throw new InvalidOperationException("Local AI service requires both endpoint address and model name to be configured in settings.");
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
            _isChatEndpoint = _endpoint.TrimEnd('/').EndsWith("completions", StringComparison.OrdinalIgnoreCase);

            var requestTimeout = int.TryParse(settings[SettingKeys.Translation.RequestTimeout],
                out var timeOut)
                ? timeOut
                : 5;
            _httpClient.Timeout = TimeSpan.FromMinutes(requestTimeout);
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (settings.TryGetValue(SettingKeys.Translation.LocalAi.ApiKey, out var apiKey) &&
                !string.IsNullOrEmpty(apiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

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

        var tokenLimitEnabled = await _settings.GetSetting(SettingKeys.Translation.TokenLimits.LocalAiTokenLimitEnabled);
        if (_tokenUsageService != null && tokenLimitEnabled == "true")
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
                return _isChatEndpoint
                    ? await TranslateWithChatApi(text, retry.Token)
                    : await TranslateWithGenerateApi(text, retry.Token);
            }
            catch (TranslationResponseException ex)
            {
                if (attempt == _maxRetries)
                {
                    _logger.LogError(ex, "Too many requests. Max retries exhausted for text: {Text}", text);
                    throw new TranslationException("Too many requests. Retry limit reached.", ex);
                }

                await Task.Delay(delay, linked.Token).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * _retryDelayMultiplier);

                _logger.LogWarning(
                    "429 Too Many Requests. Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
                    delay, attempt, _maxRetries);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable ||
                ex.StatusCode == HttpStatusCode.GatewayTimeout || ex.StatusCode == HttpStatusCode.BadGateway)
            {
                if (attempt == _maxRetries)
                {
                    _logger.LogError(ex, "LocalAI server error. Max retries exhausted for text: {Text}", text);
                    throw new TranslationException("LocalAI is temporarily unavailable. Retry limit reached.", ex);
                }

                await Task.Delay(delay, linked.Token).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * _retryDelayMultiplier);

                _logger.LogWarning(
                    "LocalAI service unavailable ({StatusCode}). Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
                    ex.StatusCode, delay, attempt, _maxRetries);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is TaskCanceledException ||
                (ex is HttpRequestException && ex.InnerException is IOException))
            {
                if (attempt == _maxRetries)
                {
                    _logger.LogError(ex, "Network error during translation. Max retries exhausted for text: {Text}", text);
                    throw new TranslationException("Network error occurred during translation.", ex);
                }

                await Task.Delay(delay, linked.Token).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * _retryDelayMultiplier);

                _logger.LogWarning(ex, "Network error (Transient). Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
                    delay, attempt, _maxRetries);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during translation attempt {Attempt}", attempt);
                throw new TranslationException("Unexpected error occurred during translation.", ex);
            }
        }

        throw new TranslationException("Translation failed after maximum retry attempts.");
    }

    /// <summary>
    /// Translates a batch of subtitles in a single API call using structured outputs fallback
    /// Since LocalAI may not support structured outputs, we'll attempt structured format first,
    /// then fall back to regular parsing if needed
    /// </summary>
    /// <param name="subtitleBatch">List of subtitles with position and content</param>
    /// <param name="sourceLanguage">Source language code</param>
    /// <param name="targetLanguage">Target language code</param>
    /// <param name="preContext">Optional context lines before the batch (currently unused for LocalAI)</param>
    /// <param name="postContext">Optional context lines after the batch (currently unused for LocalAI)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary mapping position to translated content</returns>
    public async Task<Dictionary<int, string>> TranslateBatchAsync(
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
                return await TranslateBatchWithLocalAiApi(subtitleBatch, preContext, postContext, linked.Token);
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
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable ||
                ex.StatusCode == HttpStatusCode.GatewayTimeout || ex.StatusCode == HttpStatusCode.BadGateway)
            {
                if (attempt == _maxRetries)
                {
                    _logger.LogError(ex, "Service unavailable. Max retries exhausted for batch translation");
                    throw new TranslationException("LocalAI is temporarily unavailable. Retry limit reached.", ex);
                }

                _logger.LogWarning(
                    "LocalAI service unavailable ({StatusCode}). Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
                    ex.StatusCode, delay, attempt, _maxRetries);

                await Task.Delay(delay, linked.Token).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * _retryDelayMultiplier);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is TaskCanceledException ||
                (ex is HttpRequestException && ex.InnerException is IOException))
            {
                if (attempt == _maxRetries)
                {
                    _logger.LogError(ex, "Network error during batch translation. Max retries exhausted");
                    throw new TranslationException("Network error occurred during batch translation.", ex);
                }

                _logger.LogWarning(ex, "Network error (Transient). Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
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

    private async Task<Dictionary<int, string>> TranslateBatchWithLocalAiApi(
        List<BatchSubtitleItem> subtitleBatch,
        List<string>? preContext,
        List<string>? postContext,
        CancellationToken cancellationToken)
    {
        if (!_isChatEndpoint)
        {
            return await TranslateBatchWithGenerateApi(subtitleBatch, preContext, postContext, cancellationToken);
        }

        // Try structured output first (OpenAI-compatible format)
        try
        {
            return await TranslateBatchWithStructuredOutput(subtitleBatch, preContext, postContext, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Structured output failed, falling back to JSON parsing");
            return await TranslateBatchWithJsonParsing(subtitleBatch, preContext, postContext, cancellationToken);
        }
    }

    private async Task<Dictionary<int, string>> TranslateBatchWithStructuredOutput(
        List<BatchSubtitleItem> subtitleBatch,
        List<string>? preContext,
        List<string>? postContext,
        CancellationToken cancellationToken)
    {
        var responseFormat = new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "batch_translation_response",
                strict = true,
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
                                        type = "integer",
                                        description = "Position number of the subtitle item"
                                    },
                                    sourceKey = new
                                    {
                                        type = "string",
                                        description = "Source key copied exactly from the subtitle item"
                                    },
                                    line = new
                                    {
                                        type = "string",
                                        description = "Translated subtitle text"
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

        var userContent = BuildBatchUserContent(subtitleBatch, preContext, postContext);

        var messages = new[]
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
        };

        var requestBody = new Dictionary<string, object>
        {
            ["model"] = _model!,
            ["messages"] = messages,
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

        var requestContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var stopwatch = Stopwatch.StartNew();
        var request = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = requestContent };
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        stopwatch.Stop();
        
        if (!response.IsSuccessStatusCode)
        {
            if (_dashboardService != null && response.StatusCode != HttpStatusCode.TooManyRequests)
            {
                await _dashboardService.LogApiUsage(ServiceName, null, stopwatch.ElapsedMilliseconds, false, $"Status: {response.StatusCode}");
            }

            _logger.LogError("Response Status Code: {StatusCode}", response.StatusCode);
            _logger.LogError("Response Content: {ResponseContent}",
                await response.Content.ReadAsStringAsync(cancellationToken));
            throw new TranslationException("Batch translation using LocalAI structured output failed.");
        }

        var (translatedJson, promptTokens, completionTokens, totalTokens) =
            await OpenAiStreamAccumulator.AccumulateAsync(response, cancellationToken);

        if (string.IsNullOrEmpty(translatedJson))
        {
            throw new TranslationException("Empty response received from streaming LocalAI API");
        }

        if (_dashboardService != null)
        {
            await _dashboardService.LogApiUsage(
                ServiceName,
                totalTokens,
                stopwatch.ElapsedMilliseconds,
                success: true,
                null,
                promptTokens,
                completionTokens);
        }

        try
        {
            // Parse the wrapper object first, extract the translations array
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

            return BatchTranslationResponseMapper.MapAlignedTranslationsSafe(
                subtitleBatch,
                translatedItems,
                _logger,
                ServiceName).ValidTranslations;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse structured JSON response: {Json}", translatedJson);
            throw new TranslationException("Failed to parse structured translated subtitles", ex);
        }
    }

    private async Task<Dictionary<int, string>> TranslateBatchWithJsonParsing(
        List<BatchSubtitleItem> subtitleBatch,
        List<string>? preContext,
        List<string>? postContext,
        CancellationToken cancellationToken)
    {
        var userContent = BuildBatchUserContent(subtitleBatch, preContext, postContext);

        var messages = new[]
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
        };

        var requestBody = new Dictionary<string, object>
        {
            ["model"] = _model!,
            ["messages"] = messages
        };

        requestBody = AddCustomParameters(requestBody);
        // Add streaming params — these MUST NOT be overridden by custom parameters
        requestBody["stream"] = true;
        requestBody["stream_options"] = new Dictionary<string, object>
        {
            ["include_usage"] = true
        };
        var requestContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var stopwatch = Stopwatch.StartNew();
        var request = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = requestContent };
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        stopwatch.Stop();
        
        if (!response.IsSuccessStatusCode)
        {
            if (_dashboardService != null && response.StatusCode != HttpStatusCode.TooManyRequests)
            {
                await _dashboardService.LogApiUsage(ServiceName, null, stopwatch.ElapsedMilliseconds, false, $"Status: {response.StatusCode}");
            }

            _logger.LogError("Response Status Code: {StatusCode}", response.StatusCode);
            _logger.LogError("Response Content: {ResponseContent}",
                await response.Content.ReadAsStringAsync(cancellationToken));
            throw new TranslationException("Batch translation using LocalAI JSON parsing failed.");
        }

        var (translatedJson, promptTokens, completionTokens, totalTokens) =
            await OpenAiStreamAccumulator.AccumulateAsync(response, cancellationToken);

        if (string.IsNullOrEmpty(translatedJson))
        {
            throw new TranslationException("Empty response received from streaming LocalAI API");
        }

        if (_dashboardService != null)
        {
            await _dashboardService.LogApiUsage(
                ServiceName,
                totalTokens,
                stopwatch.ElapsedMilliseconds,
                success: true,
                null,
                promptTokens,
                completionTokens);
        }

        // Try to extract JSON
        var jsonStart = translatedJson.IndexOf('[');
        var jsonEnd = translatedJson.LastIndexOf(']');
        if (jsonStart != -1 && jsonEnd != -1 && jsonEnd > jsonStart)
        {
            translatedJson = translatedJson.Substring(jsonStart, jsonEnd - jsonStart + 1);
        }

        try
        {
            var translatedItems = JsonSerializer.Deserialize<List<StructuredBatchResponse>>(translatedJson,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (translatedItems == null)
            {
                throw new TranslationException("Failed to deserialize translated subtitles from JSON parsing");
            }

            return BatchTranslationResponseMapper.MapAlignedTranslationsSafe(
                subtitleBatch,
                translatedItems,
                _logger,
                ServiceName).ValidTranslations;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse JSON response: {Json}", translatedJson);
            throw new TranslationException("Failed to parse JSON translated subtitles", ex);
        }
    }

    private async Task<Dictionary<int, string>> TranslateBatchWithGenerateApi(
        List<BatchSubtitleItem> subtitleBatch,
        List<string>? preContext,
        List<string>? postContext,
        CancellationToken cancellationToken)
    {
        var batchPrompt = _prompt +
                          "\n\nPlease return the response as a JSON array with objects containing 'position', 'sourceKey', and 'line' fields. Example: [{\"position\": 1, \"sourceKey\": \"abc123def456\", \"line\": \"translated text\"}]\n\n";

        var userContent = BuildBatchUserContent(subtitleBatch, preContext, postContext);

        var requestData = new Dictionary<string, object>
        {
            ["model"] = _model!,
            ["prompt"] = batchPrompt + userContent,
            ["stream"] = false
        };
        requestData = AddCustomParameters(requestData);

        var content = new StringContent(JsonSerializer.Serialize(requestData),
            Encoding.UTF8, "application/json");

        var stopwatch = Stopwatch.StartNew();
        var response = await _httpClient.PostAsync(_endpoint, content, cancellationToken);
        stopwatch.Stop();
        
        if (!response.IsSuccessStatusCode)
        {
            if (_dashboardService != null && response.StatusCode != HttpStatusCode.TooManyRequests)
            {
                await _dashboardService.LogApiUsage(ServiceName, null, stopwatch.ElapsedMilliseconds, false, $"Status: {response.StatusCode}");
            }

            _logger.LogError("Response Status Code: {StatusCode}", response.StatusCode);
            _logger.LogError("Response Content: {ResponseContent}",
                await response.Content.ReadAsStringAsync(cancellationToken));
            throw new TranslationException("Batch translation using Local AI generate API failed.");
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var generateResponse = JsonSerializer.Deserialize<GenerateResponse>(responseBody);

        if (_dashboardService != null)
        {
            // Generate API doesn't return usage
            await _dashboardService.LogApiUsage(ServiceName, null, stopwatch.ElapsedMilliseconds, true);
        }

        if (generateResponse == null || string.IsNullOrEmpty(generateResponse.Response))
        {
            throw new TranslationException("Invalid or empty response from generate API.");
        }

        var translatedJson = generateResponse.Response;

        // Try to extract JSON from the response
        var jsonStart = translatedJson.IndexOf('[');
        var jsonEnd = translatedJson.LastIndexOf(']');

        if (jsonStart != -1 && jsonEnd != -1 && jsonEnd > jsonStart)
        {
            translatedJson = translatedJson.Substring(jsonStart, jsonEnd - jsonStart + 1);
        }

        try
        {
            var translatedItems = JsonSerializer.Deserialize<List<StructuredBatchResponse>>(translatedJson);

            if (translatedItems == null)
            {
                throw new TranslationException("Failed to deserialize translated subtitles from generate API");
            }

            return BatchTranslationResponseMapper.MapAlignedTranslationsSafe(
                subtitleBatch,
                translatedItems,
                _logger,
                ServiceName).ValidTranslations;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse generate API JSON response: {Json}", translatedJson);
            throw new TranslationException("Failed to parse generate API translated subtitles", ex);
        }
    }

    private async Task<string> TranslateWithGenerateApi(string text, CancellationToken cancellationToken)
    {
        var requestData = new Dictionary<string, object>
        {
            ["model"] = _model!,
            ["prompt"] = _prompt + "\n\n" + text,
            ["stream"] = false
        };
        requestData = AddCustomParameters(requestData);

        var content = new StringContent(JsonSerializer.Serialize(requestData),
            Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(_endpoint, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Response Status Code: {StatusCode}", response.StatusCode);
            _logger.LogError("Response Content: {ResponseContent}",
                await response.Content.ReadAsStringAsync(cancellationToken));
            throw new TranslationException("Translation using Local AI failed.");
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var generateResponse = JsonSerializer.Deserialize<GenerateResponse>(responseBody);

        if (generateResponse == null || string.IsNullOrEmpty(generateResponse.Response))
        {
            throw new TranslationException("Invalid or empty response from generate API.");
        }

        return generateResponse.Response;
    }

    private async Task<string> TranslateWithChatApi(string? text, CancellationToken cancellationToken)
    {
        var messages = new[]
        {
            new { role = "system", content = _prompt },
            new { role = "user", content = text }
        };

        var requestBody = new Dictionary<string, object>
        {
            ["model"] = _model!,
            ["messages"] = messages
        };
        requestBody = AddCustomParameters(requestBody);

        var content = new StringContent(JsonSerializer.Serialize(requestBody),
            Encoding.UTF8, "application/json");

        var stopwatch = Stopwatch.StartNew();
        var response = await _httpClient.PostAsync(_endpoint, content, cancellationToken);
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            if (_dashboardService != null && response.StatusCode != HttpStatusCode.TooManyRequests)
            {
                await _dashboardService.LogApiUsage(ServiceName, null, stopwatch.ElapsedMilliseconds, false, $"Status: {response.StatusCode}");
            }

            _logger.LogError("Response Status Code: {StatusCode}", response.StatusCode);
            _logger.LogError("Response Content: {ResponseContent}",
                await response.Content.ReadAsStringAsync(cancellationToken));
            throw new TranslationResponseException("Translation using chat API failed.");
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var chatResponse = JsonSerializer.Deserialize<ChatResponse>(responseBody);

        if (_dashboardService != null)
        {
            await _dashboardService.LogApiUsage(
                ServiceName, 
                chatResponse?.Usage?.TotalTokens, 
                stopwatch.ElapsedMilliseconds, 
                true,
                null,
                chatResponse?.Usage?.PromptTokens,
                chatResponse?.Usage?.CompletionTokens);
        }

        if (chatResponse?.Choices == null || chatResponse.Choices.Count == 0)
        {
            throw new TranslationResponseException("Invalid or empty response from chat API.");
        }

        return chatResponse.Choices[0].Message.Content;
    }
}
