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
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromMinutes(5); // Reduced API calls to avoid spamming
    private static readonly TimeSpan MinPausePollInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxPausePollInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PaymentPauseDuration = TimeSpan.FromMinutes(5);

    private readonly ISettingService _settings;
    private readonly ILogger<ChutesUsageService> _logger;
    private readonly IMemoryCache _cache;
    private readonly HttpClient _apiClient;
    private readonly HttpClient _llmClient;
    private readonly MemoryCacheEntryOptions _snapshotCacheOptions;
    private readonly MemoryCacheEntryOptions _modelCacheOptions;
    
    private DateTime _lastApiRefresh = DateTime.MinValue;
    
    // Static fields for payment pause state (shared across all service instances)
    private static readonly object _paymentPauseLock = new();
    private static DateTime _paymentPausedUntil = DateTime.MinValue;
    
    // Static counter for local request tracking (used between API refreshes)
    private static readonly object _localCounterLock = new();
    private static int _localRequestCount = 0;
    private static DateTime _localCounterResetAt = DateTime.MinValue;

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
        while (true)
        {
            // Check for payment pause state first
            bool inPaymentPause;
            lock (_paymentPauseLock)
            {
                inPaymentPause = DateTime.UtcNow < _paymentPausedUntil;
            }
            
            if (inPaymentPause)
            {
                // For payment pauses, we DON'T trust the quota API (it may return false data).
                // Instead, just wait until the pause expires (the reset timestamp from 402 response).
                DateTime pauseUntil;
                lock (_paymentPauseLock)
                {
                    pauseUntil = _paymentPausedUntil;
                }
                
                var waitTime = pauseUntil - DateTime.UtcNow;
                if (waitTime > TimeSpan.Zero)
                {
                    _logger.LogInformation(
                        "Chutes is in payment pause state until {PauseUntil} UTC. Waiting {WaitMinutes:F1} minutes before allowing requests.",
                        pauseUntil,
                        waitTime.TotalMinutes);
                    
                    // Cap wait time to max 10 minutes per iteration to allow cancellation checks
                    var maxWait = TimeSpan.FromMinutes(10);
                    var actualWait = waitTime > maxWait ? maxWait : waitTime;
                    
                    try
                    {
                        await Task.Delay(actualWait, cancellationToken);
                    }
                    catch (TaskCanceledException)
                    {
                        throw;
                    }
                    
                    // Check if we've passed the pause time
                    if (DateTime.UtcNow < pauseUntil)
                    {
                        continue; // Still in pause, loop back
                    }
                }
                
                // Pause time has passed, clear the payment pause and re-evaluate
                lock (_paymentPauseLock)
                {
                    if (DateTime.UtcNow >= _paymentPausedUntil)
                    {
                        _paymentPausedUntil = DateTime.MinValue;
                        _logger.LogInformation("Chutes payment pause has expired. Resuming translations.");
                    }
                }
                continue;
            }
            
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

            // If Chutes reports no effective limit, don't gate requests here
            if (snapshot.AllowedRequestsPerDay <= 0)
            {
                return;
            }

            // Get buffer setting for soft limit
            var bufferSetting = await _settings.GetSetting(SettingKeys.Translation.Chutes.RequestBuffer);
            var buffer = int.TryParse(bufferSetting, out var b) && b >= 0 ? b : 50;

            // Compute remaining requests according to the snapshot
            var remaining = snapshot.AllowedRequestsPerDay - snapshot.RequestsUsed;

            // If we have headroom beyond the buffer, allow the request
            if (remaining > buffer)
            {
                // Increment local counter to reserve the slot
                snapshot.RequestsUsed++;
                _cache.Set(GetSnapshotCacheKey(modelId), snapshot, _snapshotCacheOptions);
                return;
            }

            // No headroom left inside the configured buffer:
            // pause all Chutes translations and poll Chutes usage periodically
            // until credits are available again or the operation is cancelled.
            _logger.LogWarning(
                "Chutes usage has reached the configured buffer (remaining: {Remaining}, buffer: {Buffer}, allowedPerDay: {Allowed}, used: {Used}). " +
                "Pausing Chutes translations until credits are available again.",
                remaining,
                buffer,
                snapshot.AllowedRequestsPerDay,
                snapshot.RequestsUsed);

            await WaitForQuotaResetAsync(modelId, snapshot, cancellationToken, isPaymentPause: false);
            // Loop back and re-evaluate with a fresh snapshot after waiting.
        }
    }

    /// <inheritdoc />
    public void NotifyPaymentRequired(DateTime? resetTimestamp = null)
    {
        lock (_paymentPauseLock)
        {
            // Determine pause end time: use reset timestamp if provided, otherwise use default duration
            var proposedPauseUntil = resetTimestamp ?? DateTime.UtcNow.Add(PaymentPauseDuration);
            
            // Only update if the new pause time is later than the current one
            if (proposedPauseUntil > _paymentPausedUntil)
            {
                _paymentPausedUntil = proposedPauseUntil;
                _logger.LogWarning(
                    "Chutes returned PaymentRequired (402). Pausing all Chutes translations until {PauseUntil} UTC.",
                    _paymentPausedUntil);
            }
        }
    }

    private async Task WaitForQuotaResetAsync(
        string? modelId,
        ChutesUsageSnapshot snapshot,
        CancellationToken cancellationToken,
        bool isPaymentPause = false)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Determine how long to wait before checking again.
            // Use a random interval between 1 and 10 minutes,
            // but never wait beyond the known ResetAt time if provided.
            var randomMinutes = Random.Shared.Next(
                (int)MinPausePollInterval.TotalMinutes,
                (int)MaxPausePollInterval.TotalMinutes + 1);

            var delay = TimeSpan.FromMinutes(randomMinutes);

            if (snapshot.ResetAt.HasValue)
            {
                var now = DateTime.UtcNow;
                var untilReset = snapshot.ResetAt.Value - now;

                // If the reset time is in the past or very close, use a minimal delay
                if (untilReset <= TimeSpan.Zero)
                {
                    delay = MinPausePollInterval;
                }
                else if (untilReset < delay)
                {
                    delay = untilReset;
                }
            }

            var pauseReason = isPaymentPause ? "payment required (402)" : "quota exhausted or within buffer";
            _logger.LogInformation(
                "Chutes paused ({Reason}). Waiting {DelayMinutes:F1} minutes before re-checking usage.",
                pauseReason,
                delay.TotalMinutes);

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                // Respect cancellation and propagate
                throw;
            }

            // Force a fresh snapshot from Chutes to see if credits have reset
            var refreshed = await GetUsageSnapshotAsync(modelId, forceRefresh: true, cancellationToken);

            // Get buffer setting using same logic as outer loop
            var bufferSetting = await _settings.GetSetting(SettingKeys.Translation.Chutes.RequestBuffer);
            var buffer = int.TryParse(bufferSetting, out var b) && b >= 0 ? b : 50;
            var headroom = refreshed.AllowedRequestsPerDay - refreshed.RequestsUsed;

            // If Chutes is now reporting no limit, or we have positive headroom beyond the buffer,
            // we can exit the pause loop and let EnsureRequestAllowedAsync re-evaluate.
            if (!refreshed.HasApiKey ||
                refreshed.AllowedRequestsPerDay <= 0 ||
                headroom > buffer)
            {
                _logger.LogInformation(
                    "Chutes credits appear to be available again (allowedPerDay: {Allowed}, used: {Used}, remaining: {Remaining}, headroom: {Headroom}). Resuming translations.",
                    refreshed.AllowedRequestsPerDay,
                    refreshed.RequestsUsed,
                    refreshed.RequestsRemaining,
                    headroom);
                return;
            }

            // Otherwise, continue waiting with the updated snapshot information
            snapshot = refreshed;
        }
    }

    public async Task RecordRequestAsync(string? modelId, CancellationToken cancellationToken)
    {
        // Increment local counter for accurate tracking between API refreshes
        lock (_localCounterLock)
        {
            // Reset local counter at midnight UTC for new day
            var today = DateTime.UtcNow.Date;
            if (_localCounterResetAt.Date != today)
            {
                _localRequestCount = 0;
                _localCounterResetAt = today;
            }
            _localRequestCount++;
        }
        
        // Update the cached snapshot's last synced time
        var key = GetSnapshotCacheKey(modelId);
        if (_cache.TryGetValue(key, out ChutesUsageSnapshot? snapshot) && snapshot != null)
        {
            snapshot.LastSyncedUtc = DateTime.UtcNow;
            _cache.Set(key, snapshot, _snapshotCacheOptions);
        }
        
        // Note: Don't trigger API refresh here to avoid spamming Chutes
        // API is refreshed at 5-minute intervals via EnsureRequestAllowedAsync
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
            
            // Reset local counter since we have fresh API data
            lock (_localCounterLock)
            {
                _localRequestCount = 0;
                _localCounterResetAt = DateTime.UtcNow;
            }
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
        try
        {
            // Use global subscription usage endpoint (/me) instead of per-chute endpoint
            // The per-chute endpoint only shows usage for that specific chute, not total subscription usage
            using var request = new HttpRequestMessage(HttpMethod.Get, "/users/me/quota_usage/me");
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
