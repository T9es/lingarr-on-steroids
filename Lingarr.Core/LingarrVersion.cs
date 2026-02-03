using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Lingarr.Core.Models;
using Semver;

namespace Lingarr.Core;

public static class LingarrVersion
{
    public const string Number = "2.0.0";

    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "LingarrApp" } }
    };
    private const string GitHubApiUrl = "https://api.github.com/repos/T9es/lingarr-on-steroids/releases/latest";
    private static readonly MemoryCache Cache = new(new MemoryCacheOptions());

    public static async Task<VersionInfo> CheckForUpdates()
    {
        var latestVersion = await GetLatestVersion();

        return new VersionInfo
        {
            NewVersion = IsNewVersionAvailable(latestVersion, Number),
            CurrentVersion = Number,
            LatestVersion = latestVersion
        };
    }

    private static async Task<string> GetLatestVersion()
    {
        var cacheKey = "GithubLatestRelease";
        if (Cache.TryGetValue(cacheKey, out string? cachedVersion) && cachedVersion != null)
        {
            return cachedVersion;
        }

        try
        {
            var release = await HttpClient.GetFromJsonAsync<GitHubReleaseInfo>(GitHubApiUrl);
            var latestVersion = !string.IsNullOrWhiteSpace(release?.TagName)
                ? release.TagName
                : release?.Name ?? Number;

            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromHours(24));

            Cache.Set(cacheKey, latestVersion, cacheEntryOptions);

            return latestVersion;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to get latest version, returning default application version. Error: {ex.Message}");
            return Number;
        }
    }

    private static bool IsNewVersionAvailable(string latestVersion, string currentVersion)
    {
        // Trim 'v' prefix that GitHub releases often use
        latestVersion = latestVersion?.TrimStart('v') ?? string.Empty;
        currentVersion = currentVersion?.TrimStart('v') ?? string.Empty;

        // Handle empty or null versions
        if (string.IsNullOrWhiteSpace(latestVersion) || string.IsNullOrWhiteSpace(currentVersion))
        {
            return false;
        }

        // Use Semver library for proper semantic version comparison
        if (SemVersion.TryParse(latestVersion, SemVersionStyles.Any, out var latest) &&
            SemVersion.TryParse(currentVersion, SemVersionStyles.Any, out var current))
        {
            // Compare versions: latest > current means an update is available
            // Pre-release versions are considered less than their release counterparts
            // e.g., 2.2.0-beta < 2.2.0
            return latest.ComparePrecedenceTo(current) > 0;
        }

        return false;
    }
}