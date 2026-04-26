using Lingarr.Core.Configuration;
using System.Net.Http.Json;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Batch;
using Microsoft.Extensions.Caching.Memory;

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
        ITokenUsageService? tokenUsageService = null)
        : base(settings, logger, httpClientFactory.CreateClient(nameof(NanoGptService)), dashboardService, tokenUsageService)
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

    protected override Task<string> GetChatCompletionsEndpointAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(NanoGptEndpointSelector.GetChatCompletionsEndpoint());
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
