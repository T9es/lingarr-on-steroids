using System.Text.Json;
using Lingarr.Core.Configuration;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.NanoGpt;
using Microsoft.Extensions.Caching.Memory;

namespace Lingarr.Server.Services.Translation;

public class NanoGptUsageService : INanoGptUsageService
{
    private const string ApiBaseUrl = "https://nano-gpt.com";
    private const string CacheKey = "nanogpt-usage-snapshot";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    private readonly ISettingService _settings;
    private readonly ITokenUsageService _tokenUsageService;
    private readonly ILogger<NanoGptUsageService> _logger;
    private readonly IMemoryCache _cache;
    private readonly HttpClient _httpClient;

    public NanoGptUsageService(
        ISettingService settings,
        ITokenUsageService tokenUsageService,
        ILogger<NanoGptUsageService> logger,
        IMemoryCache cache,
        IHttpClientFactory httpClientFactory)
    {
        _settings = settings;
        _tokenUsageService = tokenUsageService;
        _logger = logger;
        _cache = cache;
        _httpClient = httpClientFactory.CreateClient(nameof(NanoGptUsageService));
        _httpClient.BaseAddress = new Uri(ApiBaseUrl);
    }

    public async Task<NanoGptUsageSnapshot> GetUsageSnapshotAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!forceRefresh &&
            _cache.TryGetValue(CacheKey, out NanoGptUsageSnapshot? cached) &&
            cached != null)
        {
            return cached;
        }

        var snapshot = await FetchSnapshotAsync(cancellationToken);
        _cache.Set(CacheKey, snapshot, CacheLifetime);
        return snapshot;
    }

    public async Task EnsureUsageAvailableAsync(CancellationToken cancellationToken)
    {
        var snapshot = await GetUsageSnapshotAsync(false, cancellationToken);
        if (!snapshot.HasApiKey || !snapshot.Active)
        {
            return;
        }

        var settings = await GetReserveSettingsAsync();
        var result = NanoGptReservePolicy.Evaluate(snapshot, settings);
        if (!result.IsBlocked)
        {
            return;
        }

        var resetText = result.ResetAt.HasValue ? $" Reset at {result.ResetAt.Value:u}." : string.Empty;
        throw new TranslationException($"NanoGPT subscription reserve reached: {result.Reason}.{resetText}");
    }

    private async Task<NanoGptUsageSnapshot> FetchSnapshotAsync(CancellationToken cancellationToken)
    {
        var apiKey = await _settings.GetSetting(SettingKeys.Translation.NanoGpt.ApiKey);
        var snapshot = new NanoGptUsageSnapshot
        {
            HasApiKey = !string.IsNullOrWhiteSpace(apiKey),
            LastSyncedUtc = DateTime.UtcNow
        };

        if (!snapshot.HasApiKey || string.IsNullOrWhiteSpace(apiKey))
        {
            snapshot.Message = "NanoGPT API key is not configured.";
            await ApplyFallbackTokenWindowAsync(snapshot);
            return snapshot;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/subscription/v1/usage");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                snapshot.Message = $"Failed to fetch NanoGPT usage. Status: {response.StatusCode}";
                _logger.LogWarning("NanoGPT usage API returned {Status}: {Content}", response.StatusCode, content);
                await ApplyFallbackTokenWindowAsync(snapshot);
                return snapshot;
            }

            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content);
            snapshot = NanoGptUsageParser.Parse(document.RootElement);
            snapshot.HasApiKey = true;
            snapshot.LastSyncedUtc = DateTime.UtcNow;
            await ApplyFallbackTokenWindowAsync(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh NanoGPT usage snapshot.");
            snapshot.Message = "Unable to refresh NanoGPT usage information.";
            await ApplyFallbackTokenWindowAsync(snapshot);
        }

        return snapshot;
    }

    private async Task ApplyFallbackTokenWindowAsync(NanoGptUsageSnapshot snapshot)
    {
        if (snapshot.WeeklyTokens.Limit.HasValue)
        {
            return;
        }

        var allowanceSetting = await _settings.GetSetting(SettingKeys.Translation.NanoGpt.WeeklyTokenAllowance);
        if (long.TryParse(allowanceSetting, out var allowance) && allowance > 0)
        {
            var localUsage = await _tokenUsageService.GetUsageAsync("nanogpt");
            snapshot.WeeklyTokens.Limit = allowance;
            snapshot.WeeklyTokens.Used = localUsage.TokensUsedToday;
            snapshot.WeeklyTokens.Remaining = Math.Max(allowance - localUsage.TokensUsedToday, 0);
            snapshot.WeeklyTokens.PercentUsed = allowance > 0
                ? (double)localUsage.TokensUsedToday / allowance
                : 0;
            snapshot.WeeklyTokens.ResetAt = localUsage.ResetAt;
        }
    }

    private async Task<NanoGptReserveSettings> GetReserveSettingsAsync()
    {
        var settings = await _settings.GetSettings([
            SettingKeys.Translation.NanoGpt.DailyUnitReserve,
            SettingKeys.Translation.NanoGpt.MonthlyUnitReserve,
            SettingKeys.Translation.NanoGpt.TokenReserve
        ]);

        return new NanoGptReserveSettings
        {
            DailyUnitReserve = ParseReserve(settings[SettingKeys.Translation.NanoGpt.DailyUnitReserve]),
            MonthlyUnitReserve = ParseReserve(settings[SettingKeys.Translation.NanoGpt.MonthlyUnitReserve]),
            TokenReserve = ParseReserve(settings[SettingKeys.Translation.NanoGpt.TokenReserve])
        };
    }

    private static long ParseReserve(string? value)
    {
        return long.TryParse(value, out var parsed) && parsed > 0 ? parsed : 0;
    }
}
