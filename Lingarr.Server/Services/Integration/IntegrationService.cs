using Lingarr.Server.Interfaces.Providers;
using Lingarr.Server.Models;
using Lingarr.Server.Interfaces.Services;
using System.Text.Json;

namespace Lingarr.Server.Services.Integration;

public class IntegrationService : IIntegrationService
{
    private readonly HttpClient _httpClient;
    private readonly IIntegrationSettingsProvider _settingsProvider;

    public IntegrationService(HttpClient httpClient, IIntegrationSettingsProvider settingsProvider)
    {
        _httpClient = httpClient;
        _settingsProvider = settingsProvider;
    }
    
    public async Task<T?> GetApiResponse<T>(string apiUrl, IntegrationSettingKeys settingKeys)
    {
        var settings = await _settingsProvider.GetSettings(settingKeys);
        if (settings == null) return default;
        
        var separator = apiUrl.Contains("?") ? "&" : "?";
        var url = $"{settings.Url}{apiUrl}{separator}apikey={settings.ApiKey}";

        var response = await _httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Integration request failed: {response.StatusCode}: {errorContent}");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<T>(responseStream);
    }
    
    /// <inheritdoc />
    public async Task<IntegrationTestResult> TestConnection(IntegrationSettingKeys settingKeys)
    {
        var settings = await _settingsProvider.GetSettings(settingKeys);
        if (settings == null || string.IsNullOrEmpty(settings.Url) || string.IsNullOrEmpty(settings.ApiKey))
        {
            return new IntegrationTestResult
            {
                IsConnected = false,
                Message = "URL or API key is not configured."
            };
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var url = $"{settings.Url}/api/v3/system/status?apikey={settings.ApiKey}";
            
            var response = await _httpClient.GetAsync(url, cts.Token);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cts.Token);
                var jsonDoc = JsonDocument.Parse(content);
                var version = jsonDoc.RootElement.TryGetProperty("version", out var versionElement) 
                    ? versionElement.GetString() 
                    : null;
                
                return new IntegrationTestResult
                {
                    IsConnected = true,
                    Message = "Connection successful.",
                    Version = version
                };
            }

            var errorContent = await response.Content.ReadAsStringAsync(cts.Token);
            return new IntegrationTestResult
            {
                IsConnected = false,
                Message = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}"
            };
        }
        catch (TaskCanceledException)
        {
            return new IntegrationTestResult
            {
                IsConnected = false,
                Message = "Connection timed out after 5 seconds."
            };
        }
        catch (HttpRequestException ex)
        {
            return new IntegrationTestResult
            {
                IsConnected = false,
                Message = $"Connection error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new IntegrationTestResult
            {
                IsConnected = false,
                Message = $"Unexpected error: {ex.Message}"
            };
        }
    }
}