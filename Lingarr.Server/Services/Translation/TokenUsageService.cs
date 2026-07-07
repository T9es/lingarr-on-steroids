using System.Collections.Concurrent;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Lingarr.Server.Services.Translation;

public class TokenUsageService : ITokenUsageService
{
    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settings;
    private readonly ILogger<TokenUsageService> _logger;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _timeProvider;
    
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);
    
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _serviceLocks = new();

    public TokenUsageService(
        LingarrDbContext dbContext,
        ISettingService settings,
        ILogger<TokenUsageService> logger,
        IMemoryCache cache,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _settings = settings;
        _logger = logger;
        _cache = cache;
        _timeProvider = timeProvider;
    }

    public async Task EnsureTokensAvailableAsync(string service, CancellationToken cancellationToken)
    {
        var limitSetting = await GetTokenLimitSetting(service);
        if (string.IsNullOrWhiteSpace(limitSetting) || !long.TryParse(limitSetting, out var limit) || limit <= 0)
        {
            return;
        }

        var bounds = await GetWindowBoundsAsync();
        var usage = await GetUsageFromDb(service, bounds);

        if (usage.TokensUsedToday < limit)
        {
            return;
        }

        _logger.LogWarning(
            "Token limit reached for {Service}: {Used}/{Limit} tokens. Pausing until {ResetTime}",
            service, usage.TokensUsedToday, limit, bounds.NextResetUtc);

        await WaitForResetAsync(service, limit, bounds.NextResetUtc, cancellationToken);
    }

    public async Task RecordUsageAsync(string service, int? promptTokens, int? completionTokens)
    {
        var outputTokens = completionTokens ?? 0;
        if (outputTokens <= 0)
        {
            return;
        }

        var bounds = await GetWindowBoundsAsync();
        var cacheKey = GetCacheKey(service, bounds);
        if (_cache.TryGetValue<TokenUsageSnapshot>(cacheKey, out var snapshot) && snapshot != null)
        {
            snapshot.TokensUsedToday += outputTokens;
            snapshot.LastUpdated = _timeProvider.GetUtcNow().UtcDateTime;
            _cache.Set(cacheKey, snapshot, CacheLifetime);
        }
    }

    public async Task<TokenUsageSnapshot> GetUsageAsync(string service)
    {
        var bounds = await GetWindowBoundsAsync();
        var usage = await GetUsageFromDb(service, bounds);
        var limitSetting = await GetTokenLimitSetting(service);
        
        usage.TokenLimit = long.TryParse(limitSetting, out var limit) && limit > 0 ? limit : null;
        usage.ResetAt = bounds.NextResetUtc;
        
        return usage;
    }

    private async Task<TokenUsageSnapshot> GetUsageFromDb(string service, TokenUsageWindowBounds bounds)
    {
        var cacheKey = GetCacheKey(service, bounds);
        if (_cache.TryGetValue<TokenUsageSnapshot>(cacheKey, out var cached) && cached != null)
        {
            return cached;
        }

        var tokensUsed = await _dbContext.ApiUsageLogs
            .Where(log => log.Service == service && log.Timestamp >= bounds.WindowStartUtc)
            .SumAsync(log => log.CompletionTokens ?? log.TokensUsed ?? 0);

        var snapshot = new TokenUsageSnapshot
        {
            Service = service,
            TokensUsedToday = tokensUsed,
            LastUpdated = _timeProvider.GetUtcNow().UtcDateTime
        };

        _cache.Set(cacheKey, snapshot, CacheLifetime);
        return snapshot;
    }

    private async Task<string> GetTokenLimitSetting(string service)
    {
        var key = service.ToLowerInvariant() switch
        {
            "openai" => SettingKeys.Translation.TokenLimits.OpenAiTokenLimit,
            "anthropic" => SettingKeys.Translation.TokenLimits.AnthropicTokenLimit,
            "gemini" => SettingKeys.Translation.TokenLimits.GeminiTokenLimit,
            "deepseek" => SettingKeys.Translation.TokenLimits.DeepSeekTokenLimit,
            "localai" => SettingKeys.Translation.TokenLimits.LocalAiTokenLimit,
            "chutes" => SettingKeys.Translation.TokenLimits.ChutesTokenLimit,
            "nanogpt" => SettingKeys.Translation.TokenLimits.NanoGptTokenLimit,
            _ => null
        };

        return key != null ? await _settings.GetSetting(key) ?? string.Empty : string.Empty;
    }

    private async Task<TokenUsageWindowBounds> GetWindowBoundsAsync()
    {
        var resetTimeSetting = await _settings.GetSetting(SettingKeys.Translation.TokenLimits.TokenLimitResetTime);
        return GetWindowBounds(resetTimeSetting);
    }

    private TokenUsageWindowBounds GetWindowBounds(string? resetTimeSetting)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var resetTimeOfDay = ParseResetTime(resetTimeSetting);
        var resetTodayUtc = nowUtc.Date.Add(resetTimeOfDay);

        return nowUtc >= resetTodayUtc
            ? new TokenUsageWindowBounds(resetTodayUtc, resetTodayUtc.AddDays(1), resetTimeSetting ?? "00:00")
            : new TokenUsageWindowBounds(resetTodayUtc.AddDays(-1), resetTodayUtc, resetTimeSetting ?? "00:00");
    }

    private static TimeSpan ParseResetTime(string? resetTimeSetting)
    {
        if (string.IsNullOrWhiteSpace(resetTimeSetting))
        {
            return TimeSpan.Zero;
        }

        var parts = resetTimeSetting.Split(':');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var hours) &&
            int.TryParse(parts[1], out var minutes))
        {
            return new TimeSpan(hours, minutes, 0);
        }

        return TimeSpan.Zero;
    }

    private static string GetCacheKey(string service, TokenUsageWindowBounds bounds)
    {
        return $"token-usage-{service.ToLowerInvariant()}-{bounds.WindowStartUtc.Ticks}-{bounds.ResetTimeSetting}";
    }

    private async Task WaitForResetAsync(string service, long limit, DateTime nextResetUtc, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            if (now >= nextResetUtc)
            {
                _logger.LogInformation("Token limit reset time reached for {Service}", service);
                return;
            }

            var waitTime = nextResetUtc - now;
            var actualWait = waitTime > PollInterval ? PollInterval : waitTime;

            _logger.LogInformation(
                "Waiting {Minutes:F1} minutes for token limit reset on {Service}",
                actualWait.TotalMinutes, service);

            try
            {
                await Task.Delay(actualWait, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            var bounds = await GetWindowBoundsAsync();
            var usage = await GetUsageFromDb(service, bounds);
            if (usage.TokensUsedToday < limit)
            {
                _logger.LogInformation(
                    "Token usage dropped below limit for {Service}: {Used}/{Limit}",
                    service, usage.TokensUsedToday, limit);
                return;
            }
        }

        throw new OperationCanceledException("Token limit wait cancelled");
    }

    private sealed record TokenUsageWindowBounds(
        DateTime WindowStartUtc,
        DateTime NextResetUtc,
        string ResetTimeSetting);
}
