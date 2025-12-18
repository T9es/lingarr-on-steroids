using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lingarr.Core.Configuration;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Microsoft.Extensions.Logging;

namespace Lingarr.Server.Services.Subtitle;

public class SubdlService : ISubtitleProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SubdlService> _logger;
    private readonly ISettingService _settingService;
    private const string BaseUrl = "https://api.subdl.com/api/v1/subtitles";

    public string Name => "Subdl";

    public SubdlService(
        HttpClient httpClient,
        ILogger<SubdlService> logger,
        ISettingService settingService)
    {
        _httpClient = httpClient;
        _logger = logger;
        _settingService = settingService;
    }

    public async Task<List<SubtitleSearchResult>> SearchByHashAsync(string hash, long fileSizeBytes, CancellationToken cancellationToken)
    {
        // Subdl doesn't support hash search in the same way OS does, or documentation is sparse.
        // We will skip hash search for Subdl for now and focus on ID search.
        return new List<SubtitleSearchResult>();
    }

    public async Task<List<SubtitleSearchResult>> SearchByImdbAsync(string imdbId, int? seasonNumber, int? episodeNumber, CancellationToken cancellationToken)
    {
        var apiKey = await GetApiKey();
        if (string.IsNullOrEmpty(apiKey)) return new List<SubtitleSearchResult>();

        var url = $"{BaseUrl}?api_key={apiKey}&imdb_id={imdbId}";
        if (seasonNumber.HasValue) url += $"&season={seasonNumber}";
        if (episodeNumber.HasValue) url += $"&episode={episodeNumber}";
        // Subdl might use different endpoint for search.
        // Correct endpoint strictly for search: https://api.subdl.com/api/v1/subtitles?imdb_id=tt1234567
        
        return await ExecuteSearch(url, cancellationToken);
    }

    public async Task<List<SubtitleSearchResult>> SearchByTmdbAsync(int tmdbId, MediaType mediaType, int? seasonNumber, int? episodeNumber, CancellationToken cancellationToken)
    {
        var apiKey = await GetApiKey();
        if (string.IsNullOrEmpty(apiKey)) return new List<SubtitleSearchResult>();

        var type = mediaType == MediaType.Movie ? "movie" : "tv";
        var url = $"{BaseUrl}?api_key={apiKey}&tmdb_id={tmdbId}&type={type}";
        if (seasonNumber.HasValue) url += $"&season={seasonNumber}";
        if (episodeNumber.HasValue) url += $"&episode={episodeNumber}";

        return await ExecuteSearch(url, cancellationToken);
    }

    public async Task<List<SubtitleSearchResult>> SearchByQueryAsync(string query, int? seasonNumber, int? episodeNumber, CancellationToken cancellationToken)
    {
        // Not prioritized
        return new List<SubtitleSearchResult>();
    }

    public async Task<string?> DownloadSubtitleAsync(string downloadLink, CancellationToken cancellationToken)
    {
        // Subdl download links return ZIP files containing subtitle files
        // Logic: Download zip -> Extract to temp -> Return path to best SRT/ASS file
        
        try 
        {
            // Subdl URLs need the base domain prepended
            var fullUrl = downloadLink.StartsWith("http") 
                ? downloadLink 
                : $"https://dl.subdl.com{downloadLink}";
            
            _logger.LogDebug("Downloading subtitle ZIP from: {Url}", fullUrl);
            
            var response = await _httpClient.GetAsync(fullUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to download from Subdl: {StatusCode}", response.StatusCode);
                return null;
            }
            
            // Create temp directory for extraction
            var tempDir = Path.Combine(Path.GetTempPath(), $"subdl_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            
            var zipPath = Path.Combine(tempDir, "subtitle.zip");
            
            // Save ZIP file
            await using (var fs = File.Create(zipPath))
            {
                await response.Content.CopyToAsync(fs, cancellationToken);
            }
            
            // Extract ZIP
            ZipFile.ExtractToDirectory(zipPath, tempDir);
            
            // Find the best subtitle file (prefer SRT, then ASS)
            var subtitleFiles = Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) ||
                           f.EndsWith(".ass", StringComparison.OrdinalIgnoreCase) ||
                           f.EndsWith(".ssa", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) ? 0 : 1) // Prefer SRT
                .ToList();
            
            if (subtitleFiles.Count == 0)
            {
                _logger.LogWarning("No subtitle files found in downloaded ZIP from Subdl");
                // Cleanup temp directory
                try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
                return null;
            }
            
            var bestFile = subtitleFiles.First();
            _logger.LogInformation("Extracted subtitle from Subdl: {File}", Path.GetFileName(bestFile));
            
            // Clean up the zip file but keep the extracted subtitle
            try { File.Delete(zipPath); } catch { /* ignore */ }
            
            return bestFile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download subtitle from Subdl");
            return null;
        }
    }

    private async Task<string?> GetApiKey()
    {
        return await _settingService.GetSetting(SettingKeys.SubtitleProvider.Subdl.ApiKey);
    }

    private async Task<List<SubtitleSearchResult>> ExecuteSearch(string url, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<SubdlResponse>(url, cancellationToken);
            if (response?.Status == true && response.Subtitles != null)
            {
                return response.Subtitles.Select(s => new SubtitleSearchResult
                {
                    Provider = Name,
                    Id = s.ReleaseName ?? Guid.NewGuid().ToString(),
                    Title = s.ReleaseName ?? "Unknown",
                    Language = s.Language,
                    Format = "srt", // Subdl is mostly srt/ass
                    DownloadLink = s.Url, // Needs full link construction?
                    Score = 0, // Calculated by Manager
                    ReleaseGroup = s.ReleaseName,
                    IsHearingImpaired = s.Hi ?? false
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching Subdl");
        }
        return new List<SubtitleSearchResult>();
    }
}

public class SubdlResponse 
{
    [JsonPropertyName("status")]
    public bool Status { get; set; }
    
    [JsonPropertyName("subtitles")]
    public List<SubdlSubtitle>? Subtitles { get; set; }
}

public class SubdlSubtitle
{
    [JsonPropertyName("release_name")]
    public string? ReleaseName { get; set; }
    
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
    
    [JsonPropertyName("lang")]
    public string Language { get; set; } = string.Empty;
    
    [JsonPropertyName("hi")]
    public bool? Hi { get; set; }
}
