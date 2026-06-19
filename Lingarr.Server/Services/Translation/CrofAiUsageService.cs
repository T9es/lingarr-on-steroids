using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.CrofAi;
using Microsoft.Extensions.Caching.Memory;

namespace Lingarr.Server.Services.Translation;

public class CrofAiUsageService : ICrofAiUsageService
{
    private const string UsageApiUrl = "https://crof.ai/usage_api/";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
    private const string CacheKey = "crofai-usage-snapshot";

    private readonly ISettingService _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CrofAiUsageService> _logger;
    private readonly IMemoryCache _cache;

    // Subscription daily quota removed — crof.ai no longer offers subscriptions.
    // private static int _requestsUsed;
    // private static readonly object _counterLock = new();

    public CrofAiUsageService(
        ISettingService settings,
        IHttpClientFactory httpClientFactory,
        ILogger<CrofAiUsageService> logger,
        IMemoryCache cache)
    {
        _settings = settings;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _cache = cache;
    }

    public async Task EnsureRequestAllowedAsync(CancellationToken cancellationToken)
    {
        var snapshot = await GetUsageSnapshotAsync(false, cancellationToken);

        if (!snapshot.HasApiKey)
        {
            return;
        }

        // Subscription daily quota check removed — crof.ai no longer offers subscriptions.
        // Credits (PAYG) balance check only:
        if (snapshot.Credits.HasValue && snapshot.Credits.Value <= 0)
        {
            _logger.LogWarning("CrofAI credits exhausted ({Credits}). Cannot translate.", snapshot.Credits.Value);
            throw new InvalidOperationException("CrofAI credits exhausted. Please add more credits to continue.");
        }
    }

    public Task RecordRequestAsync(CancellationToken cancellationToken)
    {
        // Subscription daily quota removed — no-op for credits-only billing.
        // Interlocked.Increment(ref _requestsUsed);
        return Task.CompletedTask;
    }

    public async Task<CrofAiUsageSnapshot> GetUsageSnapshotAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _cache.TryGetValue(CacheKey, out CrofAiUsageSnapshot? cached) && cached != null)
        {
            return cached;
        }

        var apiKey = await _settings.GetSetting(SettingKeys.Translation.CrofAi.ApiKey);

        if (string.IsNullOrEmpty(apiKey))
        {
            var snapshot = new CrofAiUsageSnapshot
            {
                HasApiKey = false,
                LastSyncedUtc = DateTime.UtcNow,
                Message = "CrofAI API key is not configured."
            };
            CacheSnapshot(snapshot);
            return snapshot;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient(nameof(CrofAiUsageService));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            client.Timeout = TimeSpan.FromSeconds(10);

            var response = await client.GetAsync(UsageApiUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch CrofAI usage. Status: {StatusCode}", response.StatusCode);

                var fallback = new CrofAiUsageSnapshot
                {
                    HasApiKey = true,
                    LastSyncedUtc = DateTime.UtcNow,
                    Message = $"Failed to fetch usage data. Status: {response.StatusCode}"
                };
                CacheSnapshot(fallback);
                return fallback;
            }
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var usageData = JsonSerializer.Deserialize<JsonElement>(content);

            // Check for API-level error responses first
            var errorMessage = ExtractErrorMessage(usageData);
            if (errorMessage != null)
            {
                _logger.LogWarning("CrofAI usage API returned an error: {Error}", errorMessage);
                var errorSnapshot = new CrofAiUsageSnapshot
                {
                    HasApiKey = true,
                    LastSyncedUtc = DateTime.UtcNow,
                    Message = errorMessage
                };
                CacheSnapshot(errorSnapshot);
                return errorSnapshot;
            }

            // Subscription daily quota removed — crof.ai no longer offers subscriptions.
            // decimal? usableRequests = null;
            // if (usageData.ValueKind == JsonValueKind.Object &&
            //     usageData.TryGetProperty("usable_requests", out var requestsElement) &&
            //     requestsElement.ValueKind == JsonValueKind.Number)
            // {
            //     if (requestsElement.TryGetDecimal(out var parsedRequests))
            //     {
            //         usableRequests = parsedRequests;
            //     }
            //     else
            //     {
            //         _logger.LogWarning("CrofAI usable_requests value is not a valid number. Raw: {Raw}", requestsElement.GetRawText());
            //     }
            // }

            decimal? credits = null;
            if (usageData.ValueKind == JsonValueKind.Object &&
                usageData.TryGetProperty("credits", out var creditsElement) &&
                creditsElement.ValueKind == JsonValueKind.Number)
            {
                credits = creditsElement.GetDecimal();
            }

            var snapshot = new CrofAiUsageSnapshot
            {
                HasApiKey = true,
                // Subscription daily quota removed — crof.ai no longer offers subscriptions.
                // UsableRequests = usableRequests,
                Credits = credits,
                LastSyncedUtc = DateTime.UtcNow
            };

            // Subscription daily quota removed — crof.ai no longer offers subscriptions.
            // if (usableRequests == null && credits == null && usageData.ValueKind != JsonValueKind.Object)
            if (credits == null && usageData.ValueKind != JsonValueKind.Object)
            {
                snapshot.Message = "Unexpected response format from CrofAI usage API.";
            }

            CacheSnapshot(snapshot);
            return snapshot;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching CrofAI usage");

            var errorSnapshot = new CrofAiUsageSnapshot
            {
                HasApiKey = true,
                LastSyncedUtc = DateTime.UtcNow,
                Message = $"HTTP error: {ex.Message}"
            };
            CacheSnapshot(errorSnapshot);
            return errorSnapshot;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("CrofAI usage API request timed out");

            var timeoutSnapshot = new CrofAiUsageSnapshot
            {
                HasApiKey = true,
                LastSyncedUtc = DateTime.UtcNow,
                Message = "Usage API request timed out."
            };
            CacheSnapshot(timeoutSnapshot);
            return timeoutSnapshot;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching CrofAI usage");

            var errorResult = new CrofAiUsageSnapshot
            {
                HasApiKey = true,
                LastSyncedUtc = DateTime.UtcNow,
                Message = $"Error: {ex.Message}"
            };
            CacheSnapshot(errorResult);
            return errorResult;
        }
    }

    private static string? ExtractErrorMessage(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty("error", out var errorProp) &&
            errorProp.ValueKind == JsonValueKind.Object &&
            errorProp.TryGetProperty("message", out var msgProp) &&
            msgProp.ValueKind == JsonValueKind.String)
        {
            return msgProp.GetString();
        }

        if (errorProp.ValueKind == JsonValueKind.String)
        {
            return errorProp.GetString();
        }

        if (element.TryGetProperty("message", out var messageProp) &&
            messageProp.ValueKind == JsonValueKind.String)
        {
            return messageProp.GetString();
        }

        return null;
    }
    private void CacheSnapshot(CrofAiUsageSnapshot snapshot)
    {
        _cache.Set(CacheKey, snapshot, CacheLifetime);
    }
}
