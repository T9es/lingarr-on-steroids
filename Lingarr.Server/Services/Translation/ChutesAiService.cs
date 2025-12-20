using System.Net.Http.Headers;
using Lingarr.Core.Configuration;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Batch;

namespace Lingarr.Server.Services.Translation;

public class ChutesAiService : OpenAiService
{
    private readonly IChutesUsageService _usageService;

    protected override string ModelSettingKey => SettingKeys.Translation.Chutes.Model;
    protected override string ApiKeySettingKey => SettingKeys.Translation.Chutes.ApiKey;
    protected override string EndpointBase => "https://llm.chutes.ai/v1/";

    public ChutesAiService(
        ISettingService settings,
        ILogger<ChutesAiService> logger,
        IChutesUsageService usageService,
        IHttpClientFactory httpClientFactory)
        : base(settings, logger, httpClientFactory.CreateClient(nameof(ChutesAiService)))
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
        var model = await _settings.GetSetting(ModelSettingKey);
        await _usageService.EnsureRequestAllowedAsync(model, cancellationToken);

        try
        {
            var result = await base.TranslateAsync(
                text,
                sourceLanguage,
                targetLanguage,
                contextLinesBefore,
                contextLinesAfter,
                cancellationToken);

            await _usageService.RecordRequestAsync(model, cancellationToken);
            return result;
        }
        catch (TranslationException ex) when (IsPaymentRequiredError(ex))
        {
            _usageService.NotifyPaymentRequired();
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
        var model = await _settings.GetSetting(ModelSettingKey);
        await _usageService.EnsureRequestAllowedAsync(model, cancellationToken);

        try
        {
            var result = await base.TranslateBatchAsync(subtitleBatch, sourceLanguage, targetLanguage, preContext, postContext, cancellationToken);

            await _usageService.RecordRequestAsync(model, cancellationToken);
            return result;
        }
        catch (TranslationException ex) when (IsPaymentRequiredError(ex))
        {
            _usageService.NotifyPaymentRequired();
            throw;
        }
    }

    /// <summary>
    /// Checks if the exception or any of its inner exceptions indicate a PaymentRequired (402) error.
    /// </summary>
    private static bool IsPaymentRequiredError(Exception ex)
    {
        var current = ex;
        while (current != null)
        {
            if (current.Message.Contains("PaymentRequired", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            current = current.InnerException;
        }
        return false;
    }

    public override async Task<ModelsResponse> GetModels()
    {
        var apiKey = await _settings.GetSetting(ApiKeySettingKey);

        if (string.IsNullOrEmpty(apiKey))
        {
            return new ModelsResponse
            {
                Message = "Chutes API key is not configured."
            };
        }

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(EndpointBase) };
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
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
                    Message = "No models data returned from Chutes API."
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
            _logger.LogError(ex, "HTTP error fetching models from Chutes API");
            return new ModelsResponse
            {
                Message = $"HTTP error fetching models from Chutes API: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching models from Chutes API");
            return new ModelsResponse
            {
                Message = $"Error fetching models from Chutes API: {ex.Message}"
            };
        }
    }

    private static string FormatModelLabel(ModelData model)
    {
        if (model.Price?.Input?.Usd is { } input && model.Price?.Output?.Usd is { } output)
        {
            return $"{model.Id} · ${input:0.####}/${output:0.####} per MTok";
        }

        return model.Id;
    }
}
