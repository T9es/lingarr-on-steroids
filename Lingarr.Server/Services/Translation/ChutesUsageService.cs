using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lingarr.Core.Configuration;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Chutes;
using Microsoft.Extensions.Caching.Memory;

namespace Lingarr.Server.Services.Translation;

public class ChutesUsageService : IChutesUsageService
{
    private const string ApiBaseUrl = "https://api.chutes.ai";
    private const string LlmBaseUrl = "https://llm.chutes.ai/v1/";

    private static readonly TimeSpan SnapshotCacheLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ModelCacheLifetime = TimeSpan.FromHours(6);
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromMinutes(1);

    private readonly ISettingService _settings;
    private readonly ILogger<ChutesUsageService> _logger;
    private readonly IMemoryCache _cache;
    private readonly HttpClient _apiClient;
    private readonly HttpClient _llmClient;
    private readonly MemoryCacheEntryOptions _snapshotCacheOptions;
    private readonly MemoryCacheEntryOptions _modelCacheOptions;
    
    private DateTime _lastApiRefresh = DateTime.MinValue;

    public ChutesUsageService(
        ISettingService settings,
        ILogger<ChutesUsageService> logger,
        IMemoryCache cache,
        IHttpClientFactory httpClientFactory)
    {
        _settings = settings;
        _logger = logger;
        _cache = cache;

        _apiClient = httpClientFactory.CreateClient($"{nameof(ChutesUsageService)}-api");
        _apiClient.BaseAddress = new Uri(ApiBaseUrl);

        _llmClient = httpClientFactory.CreateClient($"{nameof(ChutesUsageService)}-llm");
        _llmClient.BaseAddress = new Uri(LlmBaseUrl);

        _snapshotCacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = SnapshotCacheLifetime
        };

        _modelCacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ModelCacheLifetime
        };
    }

    public async Task EnsureRequestAllowedAsync(string? modelId, CancellationToken cancellationToken)
    {
        // Rate-limited refresh: check API at most once per minute
        var shouldRefresh = DateTime.UtcNow - _lastApiRefresh >= MinRefreshInterval;
        var snapshot = await GetUsageSnapshotAsync(modelId, shouldRefresh, cancellationToken);
        if (shouldRefresh)
        {
            _lastApiRefresh = DateTime.UtcNow;
        }

        if (!snapshot.HasApiKey)
        {
            // Let the translation call fail with a clearer message later when it tries to call the API.
            return;
        }

        if (snapshot.AllowedRequestsPerDay <= 0)
        {
            return;
        }

        // Get buffer setting for soft limit
        var bufferSetting = await _settings.GetSetting(SettingKeys.Translation.Chutes.RequestBuffer);
        var buffer = int.TryParse(bufferSetting, out var b) && b >= 0 ? b : 50;

        // Soft limit check: stop if remaining requests <= buffer
        var remaining = snapshot.AllowedRequestsPerDay - snapshot.RequestsUsed;
        if (remaining <= buffer)
        {
            throw new TranslationException(
                $"Chutes usage approaching limit (remaining: {remaining}, buffer: {buffer}). " +
                "Stopping to preserve headroom.");
        }

        // Increment local counter to reserve the slot
        snapshot.RequestsUsed++;
        _cache.Set(GetSnapshotCacheKey(modelId), snapshot, _snapshotCacheOptions);
    }

    public async Task RecordRequestAsync(string? modelId, CancellationToken cancellationToken)
    {
        // Counter already incremented in EnsureRequestAllowedAsync
        // Just update the LastSyncedUtc timestamp
        var key = GetSnapshotCacheKey(modelId);
        if (_cache.TryGetValue(key, out ChutesUsageSnapshot? snapshot) && snapshot != null)
        {
            snapshot.LastSyncedUtc = DateTime.UtcNow;
            _cache.Set(key, snapshot, _snapshotCacheOptions);
        }

        // Trigger a background refresh to get the accurate count from the server
        _ = Task.Run(async () =>
        {
            try
            {
                await GetUsageSnapshotAsync(modelId, forceRefresh: true, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh Chutes usage snapshot in background.");
            }
        }, cancellationToken);
    }

    public async Task<ChutesUsageSnapshot> GetUsageSnapshotAsync(
        string? modelId,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var cacheKey = GetSnapshotCacheKey(modelId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (!forceRefresh &&
            _cache.TryGetValue(cacheKey, out ChutesUsageSnapshot? snapshot) &&
            snapshot != null &&
            snapshot.Date == today &&
            DateTime.UtcNow - snapshot.LastSyncedUtc < SnapshotCacheLifetime)
        {
            return snapshot;
        }

        var refreshed = await FetchSnapshotAsync(modelId, cancellationToken);
        _cache.Set(cacheKey, refreshed, _snapshotCacheOptions);
        return refreshed;
    }

    private async Task<ChutesUsageSnapshot> FetchSnapshotAsync(string? modelId, CancellationToken cancellationToken)
    {
        var apiKey = await _settings.GetSetting(SettingKeys.Translation.Chutes.ApiKey);
        var snapshot = new ChutesUsageSnapshot
        {
            ModelId = modelId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            LastSyncedUtc = DateTime.UtcNow,
            HasApiKey = !string.IsNullOrWhiteSpace(apiKey)
        };

        if (!snapshot.HasApiKey || string.IsNullOrWhiteSpace(apiKey))
        {
            snapshot.Message = "Chutes API key is not configured.";
            return snapshot;
        }

        try
        {
            snapshot.ChuteId = await ResolveChuteIdAsync(apiKey, modelId, cancellationToken);
            var quota = await FetchQuotaAsync(apiKey, snapshot.ChuteId, cancellationToken);
            snapshot.Plan = quota.Plan;
            snapshot.PlanRequestsPerDay = quota.PlanLimit;
            snapshot.ResetAt = quota.ResetAt;

            var overrideLimit = await GetOverrideLimitAsync();
            snapshot.OverrideRequestsPerDay = overrideLimit;

            var allowed = quota.PlanLimit ?? 0;
            if (overrideLimit.HasValue)
            {
                allowed = overrideLimit.Value;
            }

            snapshot.AllowedRequestsPerDay = allowed;

            snapshot.RemoteRequestsUsed = await FetchUsageCountAsync(apiKey, snapshot.ChuteId, cancellationToken) ?? 0;
            snapshot.RequestsUsed = snapshot.RemoteRequestsUsed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh Chutes usage snapshot.");
            snapshot.Message = "Unable to refresh usage information.";
        }

        return snapshot;
    }

    private async Task<(int? PlanLimit, string? Plan, DateTime? ResetAt)> FetchQuotaAsync(
        string apiKey,
        string? chuteId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/users/me/quotas");
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            var response = await _apiClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Chutes quota API returned {Status}: {Content}", response.StatusCode, content);
                return (null, null, null);
            }

            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content);
            return ExtractQuotaInfo(document.RootElement, chuteId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Chutes quota information.");
            return (null, null, null);
        }
    }

    private async Task<int?> FetchUsageCountAsync(string apiKey, string? chuteId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(chuteId))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/users/me/quota_usage/{chuteId}");
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            var response = await _apiClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Chutes quota usage API returned {Status}: {Content}", response.StatusCode, content);
                return null;
            }

            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content);
            return ExtractUsageCount(document.RootElement);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Chutes quota usage information.");
            return null;
        }
    }

    private async Task<string?> ResolveChuteIdAsync(string apiKey, string? modelId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        var cacheKey = $"chutes-model-{modelId}";
        if (_cache.TryGetValue(cacheKey, out string? cachedChute) && !string.IsNullOrWhiteSpace(cachedChute))
        {
            return cachedChute;
        }

        // Use a relative path 'models'. Since BaseAddress ends in 'v1/', this becomes '.../v1/models'
        using var request = new HttpRequestMessage(HttpMethod.Get, "models");
        request.Headers.Add("Authorization", $"Bearer {apiKey}");

        var response = await _llmClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var models = await response.Content.ReadFromJsonAsync<ModelsListResponse>(cancellationToken: cancellationToken);
        var chuteId = models?.Data?.FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase))
            ?.ChuteId;

        if (!string.IsNullOrWhiteSpace(chuteId))
        {
            _cache.Set(cacheKey, chuteId, _modelCacheOptions);
        }

        return chuteId;
    }

    private static (int? PlanLimit, string? Plan, DateTime? ResetAt) ExtractQuotaInfo(JsonElement root, string? chuteId)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in root.EnumerateArray())
            {
                var match = EvaluateQuotaElement(element, chuteId);
                if (match.PlanLimit.HasValue)
                {
                    return match;
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("quotas", out var quotasElement) && quotasElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in quotasElement.EnumerateArray())
                {
                    var match = EvaluateQuotaElement(element, chuteId);
                    if (match.PlanLimit.HasValue)
                    {
                        return match;
                    }
                }
            }

            var fallback = EvaluateQuotaElement(root, chuteId);
            if (fallback.PlanLimit.HasValue)
            {
                return fallback;
            }
        }

        return (null, null, null);
    }

    private static (int? PlanLimit, string? Plan, DateTime? ResetAt) EvaluateQuotaElement(JsonElement element, string? chuteId)
    {
        if (!string.IsNullOrWhiteSpace(chuteId) &&
            element.TryGetProperty("chute_id", out var chuteProp) &&
            !string.Equals(chuteProp.GetString(), chuteId, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(chuteProp.GetString(), "*", StringComparison.OrdinalIgnoreCase))
        {
            return (null, null, null);
        }

        var plan = element.TryGetProperty("plan", out var planProp) ? planProp.GetString() : null;
        var limit = TryGetInt(element, "limit_per_day", "requests_per_day", "quota");
        var resetAt = TryGetDate(element, "reset_at");

        if (!limit.HasValue && element.TryGetProperty("quota", out var quotaElement))
        {
             if(quotaElement.ValueKind == JsonValueKind.Number)
             {
                 limit = quotaElement.GetInt32();
             }
             else if(quotaElement.ValueKind == JsonValueKind.Object)
             {
                 limit = TryGetInt(quotaElement, "limit_per_day", "requests_per_day", "per_day", "limit");
                 resetAt ??= TryGetDate(quotaElement, "reset_at");
             }
        }

        return (limit, plan, resetAt);
    }

    private static int? ExtractUsageCount(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            return TryGetInt(root, "requests_used", "requests_used_today", "used", "usage");
        }

        return null;
    }

    private async Task<int?> GetOverrideLimitAsync()
    {
        var overrideSetting = await _settings.GetSetting(SettingKeys.Translation.Chutes.UsageLimitOverride);
        if (string.IsNullOrWhiteSpace(overrideSetting))
        {
            return null;
        }

        return int.TryParse(overrideSetting, out var limit) && limit > 0 ? limit : null;
    }

    private static int? TryGetInt(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.Number:
                    if (value.TryGetInt32(out var numeric))
                    {
                        return numeric;
                    }
                    if (value.TryGetDouble(out var d))
                    {
                        return (int)d;
                    }
                    break;
                case JsonValueKind.String when int.TryParse(value.GetString(), out var parsed):
                    return parsed;
                case JsonValueKind.String when double.TryParse(value.GetString(), out var parsedDouble):
                    return (int)parsedDouble;
            }
        }

        return null;
    }

    private static DateTime? TryGetDate(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String when DateTime.TryParse(value.GetString(), out var parsed) => parsed,
            JsonValueKind.Number when value.TryGetInt64(out var unix) => DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime,
            _ => null
        };
    }

    private static string GetSnapshotCacheKey(string? modelId) => $"chutes-usage-snapshot-{modelId ?? "default"}";
}
