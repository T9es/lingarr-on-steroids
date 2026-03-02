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
    
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);
    
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _serviceLocks = new();

    public TokenUsageService(
        LingarrDbContext dbContext,
        ISettingService settings,
        ILogger<TokenUsageService> logger,
        IMemoryCache cache)
    {
        _dbContext = dbContext;
        _settings = settings;
        _logger = logger;
        _cache = cache;
    }

    public async Task EnsureTokensAvailableAsync(string service, CancellationToken cancellationToken)
    {
        var limitSetting = await GetTokenLimitSetting(service);
        if (string.IsNullOrWhiteSpace(limitSetting) || !long.TryParse(limitSetting, out var limit) || limit <= 0)
        {
            return;
        }

        var resetTime = await GetResetTime();
        var usage = await GetUsageFromDb(service, resetTime);

        if (usage.TokensUsedToday < limit)
        {
            return;
        }

        _logger.LogWarning(
            "Token limit reached for {Service}: {Used}/{Limit} tokens. Pausing until {ResetTime}",
            service, usage.TokensUsedToday, limit, resetTime);

        await WaitForResetAsync(service, limit, resetTime, cancellationToken);
    }

    public async Task RecordUsageAsync(string service, int? promptTokens, int? completionTokens)
    {
        var outputTokens = completionTokens ?? 0;
        if (outputTokens <= 0) return;

        var cacheKey = $"token-usage-{service}";
        if (_cache.TryGetValue<TokenUsageSnapshot>(cacheKey, out var snapshot) && snapshot != null)
        {
            snapshot.TokensUsedToday += outputTokens;
            snapshot.LastUpdated = DateTime.UtcNow;
            _cache.Set(cacheKey, snapshot, CacheLifetime);
        }
    }

    public async Task<TokenUsageSnapshot> GetUsageAsync(string service)
    {
        var resetTime = await GetResetTime();
        var usage = await GetUsageFromDb(service, resetTime);
        var limitSetting = await GetTokenLimitSetting(service);
        
        usage.TokenLimit = long.TryParse(limitSetting, out var limit) && limit > 0 ? limit : null;
        usage.ResetAt = resetTime;
        
        return usage;
    }

    private async Task<TokenUsageSnapshot> GetUsageFromDb(string service, DateTime resetTime)
    {
        var cacheKey = $"token-usage-{service}";
        if (_cache.TryGetValue<TokenUsageSnapshot>(cacheKey, out var cached) && cached != null)
        {
            if (cached.LastUpdated > resetTime)
            {
                return cached;
            }
        }

        var tokensUsed = await _dbContext.ApiUsageLogs
            .Where(log => log.Service == service && log.Timestamp >= resetTime)
            .SumAsync(log => log.CompletionTokens ?? log.TokensUsed ?? 0);

        var snapshot = new TokenUsageSnapshot
        {
            Service = service,
            TokensUsedToday = tokensUsed,
            LastUpdated = DateTime.UtcNow
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
            _ => null
        };

        return key != null ? await _settings.GetSetting(key) : string.Empty;
    }

    private async Task<DateTime> GetResetTime()
    {
        var resetTimeSetting = await _settings.GetSetting(SettingKeys.Translation.TokenLimits.TokenLimitResetTime);
        
        if (string.IsNullOrWhiteSpace(resetTimeSetting))
        {
            return DateTime.UtcNow.Date;
        }

        var parts = resetTimeSetting.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[0], out var hours) && int.TryParse(parts[1], out var minutes))
        {
            var today = DateTime.UtcNow.Date;
            var resetToday = today.AddHours(hours).AddMinutes(minutes);
            
            return resetToday <= DateTime.UtcNow ? resetToday.AddDays(1) : resetToday;
        }

        return DateTime.UtcNow.Date;
    }

    private async Task WaitForResetAsync(string service, long limit, DateTime resetTime, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            if (now >= resetTime)
            {
                _logger.LogInformation("Token limit reset time reached for {Service}", service);
                return;
            }

            var waitTime = resetTime - now;
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

            var usage = await GetUsageFromDb(service, resetTime);
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
}
