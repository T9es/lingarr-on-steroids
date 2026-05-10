using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.IO;
using Lingarr.Core.Configuration;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Batch;

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
        ITranslationPromptAugmenter? translationPromptAugmenter = null)
        : base(settings, logger, httpClientFactory.CreateClient(nameof(CrofAiService)), dashboardService, tokenUsageService, translationPromptAugmenter)
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

        try
        {
            var result = await base.TranslateBatchAsync(
                subtitleBatch,
                sourceLanguage,
                targetLanguage,
                preContext,
                postContext,
                cancellationToken);

            await _usageService.RecordRequestAsync(cancellationToken);
            return result;
        }
        catch (TranslationException)
        {
            throw;
        }
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
            var promptPrice = model.Pricing.Prompt.Value * 1_000_000;
            var completionPrice = model.Pricing.Completion.Value * 1_000_000;
            return $"{modelName} · ${promptPrice:F2}/${completionPrice:F2} per MTok";
        }

        return modelName;
    }
}
