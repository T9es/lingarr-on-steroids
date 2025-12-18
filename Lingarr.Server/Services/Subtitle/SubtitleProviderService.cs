using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lingarr.Server.Services.Subtitle;

public class SubtitleProviderService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SubtitleProviderService> _logger;
    private readonly ISettingService _settingService;
    private readonly IServiceScopeFactory _scopeFactory;

    // In-memory circuit breaker cache (ProviderName -> CooldownUntil)
    private static readonly Dictionary<string, DateTime> CircuitBreaker = new();

    public SubtitleProviderService(
        IServiceProvider serviceProvider,
        ILogger<SubtitleProviderService> logger,
        ISettingService settingService,
        IServiceScopeFactory scopeFactory)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _settingService = settingService;
        _scopeFactory = scopeFactory;
    }

    public async Task<string?> SearchAndDownloadSubtitle(IMedia media, MediaType mediaType, string language, CancellationToken cancellationToken)
    {
        // 0. Check daily limits for all providers via DB logs
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();
        
        // Load providers
        var providers = _serviceProvider.GetServices<ISubtitleProvider>().ToList();
        var validProviders = new List<ISubtitleProvider>();

        foreach (var provider in providers)
        {
            if (IsCoolingDown(provider.Name))
            {
                _logger.LogWarning("Provider {Provider} is cooling down until {Time}", provider.Name, CircuitBreaker[provider.Name]);
                continue;
            }

            var dailyCount = await dbContext.SubtitleProviderLogs
                .CountAsync(l => l.ProviderName == provider.Name && l.CreatedAt > DateTime.UtcNow.AddHours(-24), cancellationToken);
            
            var limitSetting = await _settingService.GetSetting(SettingKeys.SubtitleProvider.DailyLimit);
            if (int.TryParse(limitSetting, out var limit) && dailyCount >= limit)
            {
                _logger.LogWarning("Provider {Provider} has reached daily limit ({Count}/{Limit}). Trip circuit breaker.", provider.Name, dailyCount, limit);
                TripCircuitBreaker(provider.Name, TimeSpan.FromHours(24));
                continue;
            }
            
            validProviders.Add(provider);
        }

        if (!validProviders.Any())
        {
            throw new SubtitleDownloadLimitException("All subtitle providers have reached their rate limits or are cooling down.");
        }

        // 1. Prepare Search Query
        // Retrieve external IDs (re-fetch media to get latest IDs)
        // ... (Mapping logic needed if we don't have IDs on IMedia interface, need to cast or fetch)
        
        string? imdbId = null;
        int? tmdbId = null;
        int? seasonNumber = null;
        int? episodeNumber = null;
        string title = media.FileName ?? "";

        if (mediaType == MediaType.Movie && media is Movie movie)
        {
             imdbId = movie.ImdbId;
             if (int.TryParse(movie.TmdbId, out var tid)) tmdbId = tid;
             title = movie.Title;
        }
        else if (mediaType == MediaType.Episode && media is Episode episode)
        {
             imdbId = episode.ImdbId;
             if (int.TryParse(episode.TmdbId, out var tid)) tmdbId = tid;
             title = episode.Title;
             seasonNumber = episode.Season.SeasonNumber;
             episodeNumber = episode.EpisodeNumber;
             // Need to fetch show IDs? Episode entity usually doesn't have show-level external IDs directly unless we denormalized or fetch relation.
             // ImdbId on Episode is usually for the episode itself. OpenSubtitles might need Show ID.
        }

        // 2. Execute Search (Tiered)
        // ... Implementation of tiered search ...
        
        // For MVP, just return null as placeholder till implementation is complete.
        return null;
    }

    private bool IsCoolingDown(string providerName)
    {
        if (CircuitBreaker.TryGetValue(providerName, out var cooldownUntil))
        {
            if (DateTime.UtcNow < cooldownUntil) return true;
            CircuitBreaker.Remove(providerName);
        }
        return false;
    }

    private void TripCircuitBreaker(string providerName, TimeSpan duration)
    {
        CircuitBreaker[providerName] = DateTime.UtcNow.Add(duration);
    }
}

public class SubtitleDownloadLimitException : Exception
{
    public SubtitleDownloadLimitException(string message) : base(message) { }
}
