using System.Net.Http.Json;
using System.Reflection;
using Microsoft.Extensions.Caching.Memory;
using Lingarr.Core.Models;
using Semver;

namespace Lingarr.Core;

public static class LingarrVersion
{
    /// <summary>
    /// Gets the current version from the assembly or falls back to a default value.
    /// </summary>
    public static string Number => GetAssemblyVersion();

    /// <summary>
    /// Gets whether this is a development build (not a tagged release).
    /// Detected by version suffix like -dev, -alpha, -beta, or commit hash presence.
    /// </summary>
    public static bool IsDevBuild => IsDevelopmentBuild();

    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "LingarrApp" } }
    };
    private const string GitHubApiUrl = "https://api.github.com/repos/T9es/lingarr-on-steroids/releases/latest";
    private static readonly MemoryCache Cache = new(new MemoryCacheOptions());

    /// <summary>
    /// Checks for available updates and returns version information.
    /// For dev builds, always returns NewVersion=false since dev builds are ahead of releases.
    /// </summary>
    public static async Task<VersionInfo> CheckForUpdates()
    {
        var latestVersion = await GetLatestVersion();
        var currentVersion = Number;

        // Dev builds should not show "update available" - they're ahead of releases
        var isNewVersion = IsDevBuild 
            ? false 
            : IsNewVersionAvailable(latestVersion, currentVersion);

        return new VersionInfo
        {
            NewVersion = isNewVersion,
            IsDevBuild = IsDevBuild,
            CurrentVersion = currentVersion,
            LatestVersion = latestVersion
        };
    }

    /// <summary>
    /// Gets the version from the assembly information.
    /// Falls back to "2.0.0-dev" if not available.
    /// </summary>
    private static string GetAssemblyVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        
        if (version != null && (version.Major > 0 || version.Minor > 0 || version.Build > 0))
        {
            // Format as Major.Minor.Build (e.g., 2.2.0)
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }

        // Fallback: try to get from AssemblyInformationalVersionAttribute
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        // Final fallback
        return "2.0.0-dev";
    }

    /// <summary>
    /// Determines if the current build is a development build based on version suffix.
    /// </summary>
    private static bool IsDevelopmentBuild()
    {
        var version = Number.ToLowerInvariant();
        
        // Check for common dev/pre-release indicators in version string
        return version.Contains("-dev") ||
               version.Contains("-alpha") ||
               version.Contains("-beta") ||
               version.Contains("-rc") ||
               version.Contains("+") || // Build metadata (e.g., git commit hash)
               version.Contains("commit") ||
               version.Contains("sha");
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