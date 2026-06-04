using Lingarr.Core.Configuration;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Models.Batch.Response;
using Microsoft.Extensions.Caching.Memory;
using Lingarr.Server.Services.Translation.Streaming;

namespace Lingarr.Server.Services.Translation;

public class NanoGptService : OpenAiService
{
    private const string ApiBase = "https://nano-gpt.com";
    private const string StandardApiBase = "https://nano-gpt.com/api/v1/";
    private const string SubscriptionModelsCacheKey = "nanogpt-models-subscription";
    private const string PaidModelsCacheKey = "nanogpt-models-paid";
    private static readonly TimeSpan ModelCacheLifetime = TimeSpan.FromMinutes(30);

    private readonly INanoGptUsageService _usageService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;

    protected override string ModelSettingKey => SettingKeys.Translation.NanoGpt.Model;
    protected override string ApiKeySettingKey => SettingKeys.Translation.NanoGpt.ApiKey;
    protected override string EndpointBase => StandardApiBase;
    protected override string ServiceName => "nanogpt";

    public NanoGptService(
        ISettingService settings,
        ILogger<NanoGptService> logger,
        INanoGptUsageService usageService,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IDashboardService? dashboardService = null,
        ITokenUsageService? tokenUsageService = null,
        ITranslationPromptAugmenter? translationPromptAugmenter = null,
        IProviderCircuitBreaker? circuitBreaker = null)
        : base(settings, logger, httpClientFactory.CreateClient(nameof(NanoGptService)), dashboardService, tokenUsageService, translationPromptAugmenter, circuitBreaker)
    {
        _usageService = usageService;
        _httpClientFactory = httpClientFactory;
        _cache = cache;
    }

    public override async Task<string> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        List<string>? contextLinesBefore,
        List<string>? contextLinesAfter,
        CancellationToken cancellationToken)
    {
        await _usageService.EnsureUsageAvailableAsync(cancellationToken);

        return await base.TranslateAsync(
            text,
            sourceLanguage,
            targetLanguage,
            contextLinesBefore,
            contextLinesAfter,
            cancellationToken);
    }

    public override async Task<Dictionary<int, string>> TranslateBatchAsync(
        List<BatchSubtitleItem> subtitleBatch,
        string sourceLanguage,
        string targetLanguage,
        List<string>? preContext,
        List<string>? postContext,
        CancellationToken cancellationToken)
    {
        await _usageService.EnsureUsageAvailableAsync(cancellationToken);

        if (await SelectedModelUsesJsonObjectBatchAsync(cancellationToken))
        {
            return await TranslateBatchWithNanoGptJsonObjectAsync(
                subtitleBatch,
                sourceLanguage,
                targetLanguage,
                preContext,
                postContext,
                cancellationToken);
        }

        if (!await SelectedModelSupportsStructuredOutputAsync(cancellationToken))
        {
            _logger.LogWarning(
                "NanoGPT model {Model} does not advertise structured output support. Falling back to individual translation.",
                _model ?? await _settings.GetSetting(ModelSettingKey));

            var translations = new Dictionary<int, string>();
            foreach (var item in subtitleBatch)
            {
                translations[item.Position] = await TranslateAsync(
                    item.Line,
                    sourceLanguage,
                    targetLanguage,
                    null,
                    null,
                    cancellationToken);
            }

            return translations;
        }

        return await base.TranslateBatchAsync(
            subtitleBatch,
            sourceLanguage,
            targetLanguage,
            preContext,
            postContext,
            cancellationToken);
    }

    public override async Task<ModelsResponse> GetModels()
    {
        var apiKey = await _settings.GetSetting(ApiKeySettingKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ModelsResponse
            {
                Message = "NanoGPT API key is not configured."
            };
        }

        try
        {
            var subscriptionModels = await FetchSubscriptionModelsAsync(apiKey, CancellationToken.None);
            var subscriptionOnly = string.Equals(
                await _settings.GetSetting(SettingKeys.Translation.NanoGpt.SubscriptionModelsOnly),
                "true",
                StringComparison.OrdinalIgnoreCase);
            var paidModels = subscriptionOnly
                ? new List<ModelData>()
                : await FetchPaidModelsAsync(apiKey, CancellationToken.None);

            return new ModelsResponse
            {
                Options = NanoGptModelCatalog.BuildModelOptions(subscriptionModels, paidModels)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching models from NanoGPT API");
            return new ModelsResponse
            {
                Message = $"Error fetching models from NanoGPT API: {ex.Message}"
            };
        }
    }

    private async Task<Dictionary<int, string>> TranslateBatchWithNanoGptJsonObjectAsync(
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
                var result = await SendNanoGptJsonObjectBatchAsync(
                    subtitleBatch,
                    sourceLanguage,
                    targetLanguage,
                    preContext,
                    postContext,
                    linked.Token);
                _circuitBreaker?.RecordSuccess(ServiceName);
                return result;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                await RecordProviderFailureAsync(ex, cancellationToken);

                if (attempt == _maxRetries)
                {
                    _logger.LogError(ex, "Too many requests. Max retries exhausted for NanoGPT batch translation");
                    throw new TranslationException("Too many requests. Retry limit reached.", ex);
                }

                _logger.LogWarning(
                    "429 Too Many Requests. Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
                    delay,
                    attempt,
                    _maxRetries);

                await Task.Delay(delay, linked.Token).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * _retryDelayMultiplier);
            }
            catch (HttpRequestException ex) when (
                ex.StatusCode == HttpStatusCode.ServiceUnavailable ||
                ex.StatusCode == HttpStatusCode.GatewayTimeout ||
                ex.StatusCode == HttpStatusCode.BadGateway)
            {
                await RecordProviderFailureAsync(ex, cancellationToken);

                if (attempt == _maxRetries)
                {
                    _logger.LogError(ex, "Service unavailable. Max retries exhausted for NanoGPT batch translation");
                    throw new TranslationException("NanoGPT is temporarily unavailable. Retry limit reached.", ex);
                }

                _logger.LogWarning(
                    "{StatusCode} Service Unavailable. Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
                    ex.StatusCode,
                    delay,
                    attempt,
                    _maxRetries);

                await Task.Delay(delay, linked.Token).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * _retryDelayMultiplier);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is SocketException ||
                ex is TaskCanceledException ||
                (ex is HttpRequestException && ex.InnerException is IOException))
            {
                if (attempt == _maxRetries)
                {
                    _logger.LogError(ex, "Network error during NanoGPT batch translation. Max retries exhausted");
                    throw new TranslationException("Network error occurred during batch translation.", ex);
                }

                _logger.LogWarning(
                    ex,
                    "Network error (Transient). Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
                    delay,
                    attempt,
                    _maxRetries);

                await Task.Delay(delay, linked.Token).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * _retryDelayMultiplier);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during NanoGPT json_object batch translation attempt {Attempt}", attempt);
                throw new TranslationException("Unexpected error occurred during batch translation.", ex);
            }
        }

        throw new TranslationException("Batch translation failed after maximum retry attempts.");
    }

    private async Task<Dictionary<int, string>> SendNanoGptJsonObjectBatchAsync(
        List<BatchSubtitleItem> subtitleBatch,
        string sourceLanguage,
        string targetLanguage,
        List<string>? preContext,
        List<string>? postContext,
        CancellationToken cancellationToken)
    {
        var requestUrl = await GetChatCompletionsEndpointAsync(cancellationToken);
        var userContent = BuildBatchUserContent(subtitleBatch, preContext, postContext);
        var systemPrompt = _prompt + "\n\n" +
                           $"Translate every subtitle line from source language '{sourceLanguage}' to target language '{targetLanguage}'. " +
                           "IMPORTANT: Return only one valid JSON object, with no markdown or explanation. " +
                           "The JSON object must match this shape exactly: " +
                           "{\"translations\":[{\"position\":1,\"sourceKey\":\"abc123def456\",\"line\":\"Translated text\"}]}. " +
                           "Every input position must appear exactly once, using the exact input position value. " +
                           "Copy sourceKey exactly from the input item for the same position. " +
                           "Do not replace positions with zero-based array indexes. " +
                           "Each line must contain only the translated subtitle text.";

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
            }
        };

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

        requestBody["response_format"] = new { type = "json_object" };
        requestBody["reasoning"] = new { exclude = true };
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
                throw new TranslationException($"Batch translation using NanoGPT API failed. Status: PaymentRequired. Response: {responseBody}");
            }

            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                _logger.LogWarning("503 Service Unavailable (Batch). Provider Message: {Content}", responseBody);
                throw new HttpRequestException("NanoGPT temporary unavailable", null, HttpStatusCode.ServiceUnavailable);
            }

            _logger.LogError(
                "NanoGPT json_object batch API failed. Status: {StatusCode}, BatchSize: {BatchSize}, Endpoint: {Endpoint}",
                response.StatusCode,
                subtitleBatch.Count,
                requestUrl);
            _logger.LogError("API Response Body: {ResponseContent}", responseBody);

            throw new TranslationException($"Batch translation using NanoGPT API failed. Status: {response.StatusCode}");
        }

        var (accumulatedJson, promptTokens, completionTokens, totalTokens) =
            await OpenAiStreamAccumulator.AccumulateAsync(response, cancellationToken);

        if (string.IsNullOrEmpty(accumulatedJson))
        {
            throw new TranslationException("Empty response received from streaming API");
        }

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

        var translatedJson = accumulatedJson;

        try
        {
            translatedJson = ExtractJsonPayload(translatedJson);
            var responseWrapper = JsonSerializer.Deserialize<JsonElement>(translatedJson);
            JsonElement translationsElement;
            if (responseWrapper.ValueKind == JsonValueKind.Array)
            {
                translationsElement = responseWrapper;
            }
            else if (!responseWrapper.TryGetProperty("translations", out translationsElement))
            {
                throw new TranslationException("Response does not contain 'translations' property");
            }

            var directTranslations = TryMapStringArrayResponse(translationsElement, subtitleBatch);
            if (directTranslations != null)
            {
                TranslationEchoGuard.ThrowIfMostlyEchoed(
                    subtitleBatch,
                    directTranslations,
                    sourceLanguage,
                    targetLanguage,
                    "NanoGPT");
                ThrowIfMostlyEmptyTranslations(subtitleBatch, directTranslations, "NanoGPT");
                TranslationLanguageGuard.ThrowIfTargetLanguageMismatch(
                    subtitleBatch,
                    directTranslations,
                    targetLanguage,
                    "NanoGPT");
                return directTranslations;
            }

            if (translationsElement.ValueKind != JsonValueKind.Array)
            {
                throw new TranslationException("Response 'translations' property is not an array");
            }

            ValidateStructuredTranslationItems(translationsElement);
            var translatedItems = JsonSerializer.Deserialize<List<StructuredBatchResponse>>(
                translationsElement.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (translatedItems == null)
            {
                throw new TranslationException("Failed to deserialize translated subtitles");
            }

            _logger.LogDebug(
                "NanoGPT json_object batch translation successful. Requested: {RequestedCount}, Received: {ReceivedCount}",
                subtitleBatch.Count,
                translatedItems.Count);

            var zeroBasedTranslations = TryMapZeroBasedPositions(translatedItems, subtitleBatch);
            if (zeroBasedTranslations != null)
            {
                TranslationEchoGuard.ThrowIfMostlyEchoed(
                    subtitleBatch,
                    zeroBasedTranslations,
                    sourceLanguage,
                    targetLanguage,
                    "NanoGPT");
                ThrowIfMostlyEmptyTranslations(subtitleBatch, zeroBasedTranslations, "NanoGPT");
                TranslationLanguageGuard.ThrowIfTargetLanguageMismatch(
                    subtitleBatch,
                    zeroBasedTranslations,
                    targetLanguage,
                    "NanoGPT");
                return zeroBasedTranslations;
            }

            var requestedPositions = subtitleBatch.Select(item => item.Position).ToHashSet();
            var unexpectedPositions = translatedItems
                .Select(item => item.Position)
                .Where(position => !requestedPositions.Contains(position))
                .Distinct()
                .ToList();
            if (unexpectedPositions.Count > 0)
            {
                _logger.LogWarning(
                    "NanoGPT json_object batch response included {UnexpectedCount} out-of-batch position(s): {Positions}",
                    unexpectedPositions.Count,
                    string.Join(", ", unexpectedPositions.Take(10)));
            }

            var mappingResult = BatchTranslationResponseMapper.MapAlignedTranslationsSafe(
                subtitleBatch,
                translatedItems,
                _logger,
                ServiceName);
            var validTranslations = mappingResult.ValidTranslations;

            if (validTranslations.Count == 0 && subtitleBatch.Count > 0)
            {
                throw new TranslationException("NanoGPT response did not contain translations for the requested subtitle positions");
            }

            if (validTranslations.Count < subtitleBatch.Count)
            {
                var receivedPositions = validTranslations.Keys.ToHashSet();
                var missingPositions = requestedPositions.Except(receivedPositions).ToList();

                _logger.LogWarning(
                    "Partial NanoGPT translation received. Missing {MissingCount} items at positions: {Positions}",
                    missingPositions.Count,
                    string.Join(", ", missingPositions.Take(10)));
            }

            TranslationEchoGuard.ThrowIfMostlyEchoed(
                subtitleBatch,
                validTranslations,
                sourceLanguage,
                targetLanguage,
                "NanoGPT");
            ThrowIfMostlyEmptyTranslations(subtitleBatch, validTranslations, "NanoGPT");
            TranslationLanguageGuard.ThrowIfTargetLanguageMismatch(
                subtitleBatch,
                validTranslations,
                targetLanguage,
                "NanoGPT");
            return validTranslations;
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "Failed to parse NanoGPT json_object batch response. BatchSize: {BatchSize}, Response: {Json}",
                subtitleBatch.Count,
                translatedJson.Substring(0, Math.Min(500, translatedJson.Length)));
            throw new TranslationException("Failed to parse translated subtitles", ex);
        }
    }

    private static Dictionary<int, string>? TryMapStringArrayResponse(
        JsonElement translationsElement,
        List<BatchSubtitleItem> subtitleBatch)
    {
        if (translationsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var elements = translationsElement.EnumerateArray().ToList();
        if (elements.Count == 0 || elements.Any(element => element.ValueKind != JsonValueKind.String))
        {
            return null;
        }

        if (elements.Count != subtitleBatch.Count)
        {
            throw new TranslationException(
                "NanoGPT returned a string array with a different item count than the requested batch");
        }

        return elements
            .Select((element, index) => new
            {
                Position = subtitleBatch[index].Position,
                Line = element.GetString() ?? string.Empty
            })
            .ToDictionary(item => item.Position, item => item.Line);
    }

    private static void ValidateStructuredTranslationItems(JsonElement translationsElement)
    {
        if (translationsElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var element in translationsElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!TryGetPropertyIgnoreCase(element, "position", out var positionElement) ||
                positionElement.ValueKind != JsonValueKind.Number ||
                !TryGetPropertyIgnoreCase(element, "sourceKey", out var sourceKeyElement) ||
                sourceKeyElement.ValueKind != JsonValueKind.String ||
                !TryGetPropertyIgnoreCase(element, "line", out var lineElement) ||
                lineElement.ValueKind != JsonValueKind.String)
            {
                throw new TranslationException(
                    "NanoGPT response contains translation objects without required numeric 'position', string 'sourceKey', and string 'line' properties");
            }
        }
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement propertyElement)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                propertyElement = property.Value;
                return true;
            }
        }

        propertyElement = default;
        return false;
    }

    private static void ThrowIfMostlyEmptyTranslations(
        IReadOnlyList<BatchSubtitleItem> sourceItems,
        IReadOnlyDictionary<int, string> translations,
        string providerName)
    {
        var comparablePositions = sourceItems
            .Where(item => !string.IsNullOrWhiteSpace(item.Line))
            .Select(item => item.Position)
            .ToList();
        if (comparablePositions.Count < 4)
        {
            return;
        }

        var emptyCount = comparablePositions.Count(position =>
            !translations.TryGetValue(position, out var translated) ||
            string.IsNullOrWhiteSpace(translated));
        var emptyRatio = (double)emptyCount / comparablePositions.Count;
        if (emptyRatio < 0.8)
        {
            return;
        }

        throw new TranslationException(
            $"{providerName} response did not contain usable translated text. Empty comparable cues: {emptyCount}/{comparablePositions.Count} ({emptyRatio:P0}).");
    }

    private static Dictionary<int, string>? TryMapZeroBasedPositions(
        List<StructuredBatchResponse> translatedItems,
        List<BatchSubtitleItem> subtitleBatch)
    {
        if (translatedItems.Count != subtitleBatch.Count)
        {
            return null;
        }

        var positions = translatedItems
            .Select(item => item.Position)
            .ToList();
        var hasExactZeroBasedPositions = positions
            .Distinct()
            .OrderBy(position => position)
            .SequenceEqual(Enumerable.Range(0, subtitleBatch.Count));
        if (!hasExactZeroBasedPositions)
        {
            return null;
        }

        foreach (var item in translatedItems)
        {
            if (item.Position < 0 || item.Position >= subtitleBatch.Count)
            {
                return null;
            }

            var requested = subtitleBatch[item.Position];
            var requestedSourceKey = BatchTranslationResponseMapper.GetSourceKey(requested);
            if (string.IsNullOrWhiteSpace(item.SourceKey) ||
                !string.Equals(item.SourceKey, requestedSourceKey, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return translatedItems.ToDictionary(
            item => subtitleBatch[item.Position].Position,
            item => item.Line);
    }

    protected override async Task<string> GetChatCompletionsEndpointAsync(CancellationToken cancellationToken)
    {
        var subscriptionIncluded = await IsSelectedModelSubscriptionIncludedAsync(cancellationToken);
        return NanoGptEndpointSelector.GetChatCompletionsEndpoint(subscriptionIncluded);
    }

    protected override async Task EnrichChatCompletionRequestAsync(
        Dictionary<string, object> requestBody,
        CancellationToken cancellationToken)
    {
        var subscriptionIncluded = await IsSelectedModelSubscriptionIncludedAsync(cancellationToken);
        var billingMode = NanoGptEndpointSelector.GetBillingMode(subscriptionIncluded);

        requestBody.Remove("billing_mode");
        requestBody.Remove("billingMode");

        if (!string.IsNullOrWhiteSpace(billingMode))
        {
            requestBody["billing_mode"] = billingMode;
        }
    }

    private async Task<bool> SelectedModelSupportsStructuredOutputAsync(CancellationToken cancellationToken)
    {
        var modelId = _model ?? await _settings.GetSetting(ModelSettingKey);
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return true;
        }

        var model = await FindModelAsync(modelId, cancellationToken);
        return NanoGptModelCatalog.SupportsStructuredOutput(model);
    }

    private async Task<bool> SelectedModelUsesJsonObjectBatchAsync(CancellationToken cancellationToken)
    {
        var modelId = _model ?? await _settings.GetSetting(ModelSettingKey);
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return false;
        }

        if (NanoGptModelCatalog.UsesJsonObjectBatch(modelId))
        {
            return true;
        }

        var model = await FindModelAsync(modelId, cancellationToken);
        return NanoGptModelCatalog.UsesJsonObjectBatch(model);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static string ExtractJsonPayload(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[7..].Trim();
        }
        else if (trimmed.StartsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[3..].Trim();
        }

        if (trimmed.EndsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^3].Trim();
        }

        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            return trimmed;
        }

        var objectStart = trimmed.IndexOf('{');
        var objectEnd = trimmed.LastIndexOf('}');
        if (objectStart >= 0 && objectEnd > objectStart)
        {
            return trimmed[objectStart..(objectEnd + 1)];
        }

        var arrayStart = trimmed.IndexOf('[');
        var arrayEnd = trimmed.LastIndexOf(']');
        if (arrayStart >= 0 && arrayEnd > arrayStart)
        {
            return trimmed[arrayStart..(arrayEnd + 1)];
        }

        return trimmed;
    }

    private async Task<bool> IsSelectedModelSubscriptionIncludedAsync(CancellationToken cancellationToken)
    {
        var modelId = _model ?? await _settings.GetSetting(ModelSettingKey);
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return false;
        }

        var apiKey = await _settings.GetSetting(ApiKeySettingKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        var models = await FetchSubscriptionModelsAsync(apiKey, cancellationToken);
        return models.Any(model => string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<ModelData?> FindModelAsync(string modelId, CancellationToken cancellationToken)
    {
        var apiKey = await _settings.GetSetting(ApiKeySettingKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var subscriptionModels = await FetchSubscriptionModelsAsync(apiKey, cancellationToken);
        var model = subscriptionModels.FirstOrDefault(model =>
            string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));
        if (model != null)
        {
            return model;
        }

        var paidModels = await FetchPaidModelsAsync(apiKey, cancellationToken);
        return paidModels.FirstOrDefault(model =>
            string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));
    }

    private Task<List<ModelData>> FetchSubscriptionModelsAsync(string apiKey, CancellationToken cancellationToken)
    {
        return FetchModelsAsync(
            SubscriptionModelsCacheKey,
            apiKey,
            "/api/subscription/v1/models?detailed=true",
            cancellationToken);
    }

    private Task<List<ModelData>> FetchPaidModelsAsync(string apiKey, CancellationToken cancellationToken)
    {
        return FetchModelsAsync(
            PaidModelsCacheKey,
            apiKey,
            "/api/paid/v1/models?detailed=true",
            cancellationToken);
    }

    private async Task<List<ModelData>> FetchModelsAsync(
        string cacheKey,
        string apiKey,
        string path,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(cacheKey, out List<ModelData>? cached) && cached != null)
        {
            return cached;
        }

        var client = _httpClientFactory.CreateClient($"{nameof(NanoGptService)}-{cacheKey}");
        client.BaseAddress = new Uri(ApiBase);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new TranslationException($"NanoGPT models API failed. Status: {response.StatusCode}. Response: {content}");
        }

        var models = await response.Content.ReadFromJsonAsync<ModelsListResponse>(cancellationToken: cancellationToken);
        var data = models?.Data ?? [];
        _cache.Set(cacheKey, data, ModelCacheLifetime);
        return data;
    }
}
