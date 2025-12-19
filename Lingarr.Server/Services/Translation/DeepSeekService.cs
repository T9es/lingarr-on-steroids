using System.Net.Http.Headers;
using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;

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
}