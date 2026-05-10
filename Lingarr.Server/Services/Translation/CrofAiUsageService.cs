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

    private static int _requestsUsed;
    private static readonly object _counterLock = new();

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

        var currentRequestsUsed = Volatile.Read(ref _requestsUsed);

        if (snapshot.UsableRequests.HasValue)
        {
            if (currentRequestsUsed >= snapshot.UsableRequests.Value)
            {
                _logger.LogWarning("CrofAI daily request limit reached ({Used}/{Total}). Waiting for reset.",
                    currentRequestsUsed, snapshot.UsableRequests.Value);
                throw new InvalidOperationException("CrofAI daily request limit reached. Please wait for reset or add more credits.");
            }
        }
        else if (snapshot.Credits.HasValue && snapshot.Credits.Value <= 0)
        {
            _logger.LogWarning("CrofAI credits exhausted ({Credits}). Cannot translate.", snapshot.Credits.Value);
            throw new InvalidOperationException("CrofAI credits exhausted. Please add more credits to continue.");
        }
    }

    public Task RecordRequestAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requestsUsed);
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

            int? usableRequests = null;
            if (usageData.TryGetProperty("usable_requests", out var requestsElement) &&
                requestsElement.ValueKind == JsonValueKind.Number)
            {
                usableRequests = requestsElement.GetInt32();
            }

            decimal? credits = null;
            if (usageData.TryGetProperty("credits", out var creditsElement) &&
                creditsElement.ValueKind == JsonValueKind.Number)
            {
                credits = creditsElement.GetDecimal();
            }

            var snapshot = new CrofAiUsageSnapshot
            {
                HasApiKey = true,
                UsableRequests = usableRequests,
                Credits = credits,
                LastSyncedUtc = DateTime.UtcNow
            };

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

    private void CacheSnapshot(CrofAiUsageSnapshot snapshot)
    {
        _cache.Set(CacheKey, snapshot, CacheLifetime);
    }
}
