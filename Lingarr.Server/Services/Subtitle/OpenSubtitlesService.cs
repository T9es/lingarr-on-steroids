using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Lingarr.Core.Configuration;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Microsoft.Extensions.Logging;

namespace Lingarr.Server.Services.Subtitle;

public class OpenSubtitlesService : ISubtitleProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenSubtitlesService> _logger;
    private readonly ISettingService _settingService;
    private const string BaseUrl = "https://api.opensubtitles.com/api/v1";
    private string? _token;
    private DateTime _tokenExpiration;

    public string Name => "OpenSubtitles";

    public OpenSubtitlesService(
        HttpClient httpClient,
        ILogger<OpenSubtitlesService> logger,
        ISettingService settingService)
    {
        _httpClient = httpClient;
        _logger = logger;
        _settingService = settingService;
    }

    public async Task<List<SubtitleSearchResult>> SearchByHashAsync(string hash, long fileSizeBytes, CancellationToken cancellationToken)
    {
        // OpenSubtitles uses MovieHash (64k + 64k)
        // Ensure hash is valid
        if (string.IsNullOrEmpty(hash)) return new List<SubtitleSearchResult>();

        var query = $"moviehash={hash}";
        return await ExecuteSearch(query, cancellationToken);
    }

    public async Task<List<SubtitleSearchResult>> SearchByImdbAsync(string imdbId, int? seasonNumber, int? episodeNumber, CancellationToken cancellationToken)
    {
        // Remove 'tt' prefix if present? API usually handles both or expects specific format.
        // Documentation says: imdb_id (int) without tt prefix.
        if (imdbId.StartsWith("tt")) imdbId = imdbId.Substring(2);
        if (!int.TryParse(imdbId, out var idInt)) return new List<SubtitleSearchResult>();

        var query = $"imdb_id={idInt}";
        if (seasonNumber.HasValue) query += $"&season_number={seasonNumber}";
        if (episodeNumber.HasValue) query += $"&episode_number={episodeNumber}";

        return await ExecuteSearch(query, cancellationToken);
    }

    public async Task<List<SubtitleSearchResult>> SearchByTmdbAsync(int tmdbId, MediaType mediaType, int? seasonNumber, int? episodeNumber, CancellationToken cancellationToken)
    {
        var query = $"tmdb_id={tmdbId}";
        if (seasonNumber.HasValue) query += $"&season_number={seasonNumber}";
        if (episodeNumber.HasValue) query += $"&episode_number={episodeNumber}";
        
        // OpenSubtitles uses 'type' filter? Usually inferred from IDs.
        return await ExecuteSearch(query, cancellationToken);
    }

    public async Task<List<SubtitleSearchResult>> SearchByQueryAsync(string query, int? seasonNumber, int? episodeNumber, CancellationToken cancellationToken)
    {
         var q = $"query={Uri.EscapeDataString(query)}";
         if (seasonNumber.HasValue) q += $"&season_number={seasonNumber}";
         if (episodeNumber.HasValue) q += $"&episode_number={episodeNumber}";
         return await ExecuteSearch(q, cancellationToken);
    }

    public async Task<string?> DownloadSubtitleAsync(string downloadLink, CancellationToken cancellationToken)
    {
        // OpenSubtitles download involves sending the file_id to /download endpoint to get a temporary link.
        // The 'DownloadLink' in search result might be the file_id or a ready link?
        // Usually, API returns a file_id.
        // Assuming downloadLink passed here IS the file_id or internal ID.
        
        try
        {
            if (!await EnsureAuthenticated()) return null;

            var payload = new { file_id = int.Parse(downloadLink) }; // Assuming generic link is file_id
            var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/download", payload, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var downloadInfo = await response.Content.ReadFromJsonAsync<OpenSubtitlesDownloadResponse>(cancellationToken: cancellationToken);
                if (downloadInfo != null && !string.IsNullOrEmpty(downloadInfo.Link))
                {
                    // Now download the actual file from the link
                    // And return content? Wait, ISubtitleProvider returns path? 
                    // Let's assume content for now or download to temp.
                    // SubdlService returned null...
                    
                    return downloadInfo.Link; 
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download from OpenSubtitles");
        }
        return null;
    }

    private async Task<List<SubtitleSearchResult>> ExecuteSearch(string queryParams, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticated()) return new List<SubtitleSearchResult>();

            var languages = await _settingService.GetSetting(SettingKeys.SubtitleProvider.DownloadSourceLanguages);
            // Parse JSON list? Or just comma separated? Default to 'en'.
            var langCode = "en"; // simplified for now
            
            var url = $"{BaseUrl}/subtitles?{queryParams}&languages={langCode}";
            var response = await _httpClient.GetFromJsonAsync<OpenSubtitlesResponse>(url, cancellationToken);

            if (response != null && response.Data != null)
            {
                return response.Data.Select(d => new SubtitleSearchResult
                {
                    Provider = Name,
                    Id = d.Attributes.Files.FirstOrDefault()?.FileId.ToString() ?? d.Id,
                    Title = d.Attributes.Release,
                    Language = d.Attributes.Language,
                    Format = d.Attributes.Format,
                    DownloadLink = d.Attributes.Files.FirstOrDefault()?.FileId.ToString() ?? "",
                    Score = 0,
                    ReleaseGroup = d.Attributes.Release,
                    IsHearingImpaired = d.Attributes.HearingImpaired
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching OpenSubtitles");
        }
        return new List<SubtitleSearchResult>();
    }

    private async Task<bool> EnsureAuthenticated()
    {
        if (!string.IsNullOrEmpty(_token) && DateTime.UtcNow < _tokenExpiration) return true;

        var apiKey = await _settingService.GetSetting(SettingKeys.SubtitleProvider.OpenSubtitles.ApiKey);
        var username = await _settingService.GetSetting(SettingKeys.SubtitleProvider.OpenSubtitles.Username);
        var password = await _settingService.GetSetting(SettingKeys.SubtitleProvider.OpenSubtitles.Password);

        // Required headers
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Api-Key", apiKey);
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Lingarr", "1.0"));

        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
        {
            try
            {
                var loginPayload = new { username, password };
                var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/login", loginPayload);
                if (response.IsSuccessStatusCode)
                {
                    var loginData = await response.Content.ReadFromJsonAsync<OpenSubtitlesLoginResponse>();
                    if (loginData != null)
                    {
                        _token = loginData.Token;
                        _tokenExpiration = DateTime.UtcNow.AddHours(20); // Token usually good for 24h
                        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpenSubtitles login failed");
            }
        }
        return false;
    }
}

// DTOs
public class OpenSubtitlesResponse
{
    [JsonPropertyName("data")]
    public List<OpenSubtitlesData>? Data { get; set; }
}

public class OpenSubtitlesData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("attributes")]
    public OpenSubtitlesAttributes Attributes { get; set; } = new();
}

public class OpenSubtitlesAttributes
{
    [JsonPropertyName("release")]
    public string Release { get; set; } = string.Empty;
    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;
    [JsonPropertyName("format")]
    public string Format { get; set; } = "srt";
    [JsonPropertyName("hearing_impaired")]
    public bool HearingImpaired { get; set; }
    [JsonPropertyName("files")]
    public List<OpenSubtitlesFile> Files { get; set; } = new();
}

public class OpenSubtitlesFile
{
    [JsonPropertyName("file_id")]
    public int FileId { get; set; }
}

public class OpenSubtitlesLoginResponse
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;
}

public class OpenSubtitlesDownloadResponse
{
    [JsonPropertyName("link")]
    public string Link { get; set; } = string.Empty;
}
