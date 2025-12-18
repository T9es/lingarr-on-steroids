using System.Collections.Concurrent;
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

public class SubtitleProviderService : ISubtitleProviderService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SubtitleProviderService> _logger;
    private readonly ISettingService _settingService;
    private readonly IServiceScopeFactory _scopeFactory;

    // In-memory circuit breaker cache (ProviderName -> CooldownUntil) - thread-safe
    private static readonly ConcurrentDictionary<string, DateTime> CircuitBreaker = new();

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

        // 2. Execute Tiered Search
        List<SubtitleSearchResult> results = new();
        
        foreach (var provider in validProviders)
        {
            try
            {
                // Tier 1: Search by IMDB ID (most accurate)
                if (!string.IsNullOrEmpty(imdbId))
                {
                    _logger.LogDebug("Searching {Provider} by IMDB ID: {ImdbId}", provider.Name, imdbId);
                    var imdbResults = await provider.SearchByImdbAsync(imdbId, seasonNumber, episodeNumber, cancellationToken);
                    if (imdbResults.Any())
                    {
                        results.AddRange(imdbResults.Where(r => r.Language.Equals(language, StringComparison.OrdinalIgnoreCase)));
                    }
                }
                
                // Tier 2: Search by TMDB ID if IMDB didn't yield results
                if (!results.Any() && tmdbId.HasValue)
                {
                    _logger.LogDebug("Searching {Provider} by TMDB ID: {TmdbId}", provider.Name, tmdbId);
                    var tmdbResults = await provider.SearchByTmdbAsync(tmdbId.Value, mediaType, seasonNumber, episodeNumber, cancellationToken);
                    if (tmdbResults.Any())
                    {
                        results.AddRange(tmdbResults.Where(r => r.Language.Equals(language, StringComparison.OrdinalIgnoreCase)));
                    }
                }
                
                // Tier 3: Search by title query as fallback
                if (!results.Any() && !string.IsNullOrEmpty(title))
                {
                    _logger.LogDebug("Searching {Provider} by title: {Title}", provider.Name, title);
                    var queryResults = await provider.SearchByQueryAsync(title, seasonNumber, episodeNumber, cancellationToken);
                    if (queryResults.Any())
                    {
                        results.AddRange(queryResults.Where(r => r.Language.Equals(language, StringComparison.OrdinalIgnoreCase)));
                    }
                }
                
                if (results.Any())
                {
                    _logger.LogInformation("Found {Count} subtitle results from {Provider}", results.Count, provider.Name);
                    break; // Use first provider that returns results
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error searching {Provider}, trying next provider", provider.Name);
            }
        }
        
        if (!results.Any())
        {
            _logger.LogInformation("No subtitle results found for {Title} in language {Language}", title, language);
            return null;
        }
        
        // 3. Score and select best result
        var minScoreSetting = await _settingService.GetSetting(SettingKeys.SubtitleProvider.MinimumMatchScore);
        var minScore = int.TryParse(minScoreSetting, out var ms) ? ms : 0;
        
        var bestResult = results
            .Where(r => r.Score >= minScore)
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.IsHearingImpaired ? 1 : 0) // Prefer non-HI
            .FirstOrDefault();
            
        if (bestResult == null)
        {
            _logger.LogInformation("No subtitle results met minimum score {MinScore}", minScore);
            return null;
        }
        
        // 4. Download the subtitle
        var provider2 = validProviders.FirstOrDefault(p => p.Name == bestResult.Provider);
        if (provider2 == null)
        {
            _logger.LogError("Provider {Provider} not found for download", bestResult.Provider);
            return null;
        }
        
        _logger.LogInformation("Downloading subtitle from {Provider}: {Title}", bestResult.Provider, bestResult.Title);
        
        var downloadedPath = await provider2.DownloadSubtitleAsync(bestResult.DownloadLink, cancellationToken);
        
        if (!string.IsNullOrEmpty(downloadedPath))
        {
            // Log the successful download
            dbContext.SubtitleProviderLogs.Add(new SubtitleProviderLog
            {
                MediaId = media.Id,
                MediaType = mediaType.ToString(),
                ProviderName = bestResult.Provider,
                Message = $"Downloaded subtitle: {bestResult.Title}",
                Level = "Info",
                CreatedAt = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation("Successfully downloaded subtitle to: {Path}", downloadedPath);
        }
        
        return downloadedPath;
    }

    private bool IsCoolingDown(string providerName)
    {
        if (CircuitBreaker.TryGetValue(providerName, out var cooldownUntil))
        {
            if (DateTime.UtcNow < cooldownUntil) return true;
            CircuitBreaker.TryRemove(providerName, out _);
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
