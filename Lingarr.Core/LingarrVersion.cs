using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Lingarr.Core.Models;
using Semver;

namespace Lingarr.Core;

public static class LingarrVersion
{
    private sealed class LocalBuildInfo
    {
        public string ReleaseVersion { get; init; } = string.Empty;
        public string DisplayVersion { get; init; } = string.Empty;
        public bool IsDevBuild { get; init; }
        public string? BranchName { get; init; }
        public string? CommitSha { get; init; }
        public string? BaseTag { get; init; }
        public int? CommitsSinceTag { get; init; }
    }

    /// <summary>
    /// Gets the current release version from the assembly or falls back to a default value.
    /// </summary>
    public static string Number => GetLocalBuildInfo().ReleaseVersion;

    /// <summary>
    /// Gets whether this is a development build (not a tagged release).
    /// </summary>
    public static bool IsDevBuild => GetLocalBuildInfo().IsDevBuild;

    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "LingarrApp" } }
    };

    private const string GitHubApiUrl = "https://api.github.com/repos/T9es/lingarr-on-steroids/releases/latest";
    private static readonly MemoryCache Cache = new(new MemoryCacheOptions());
    private static readonly Regex GitDescribeRegex =
        new(@"^(?<tag>.+)-(?<count>\d+)-g(?<sha>[0-9a-fA-F]+)$", RegexOptions.Compiled);

    /// <summary>
    /// Checks for available updates and returns version information.
    /// For dev builds, this compares only against the latest tagged GitHub release.
    /// </summary>
    public static async Task<VersionInfo> CheckForUpdates()
    {
        var localBuild = GetLocalBuildInfo();
        var latestVersion = await GetLatestVersion();

        var isNewVersion = localBuild.IsDevBuild
            ? false
            : IsNewVersionAvailable(latestVersion, localBuild.ReleaseVersion);

        return new VersionInfo
        {
            NewVersion = isNewVersion,
            IsDevBuild = localBuild.IsDevBuild,
            CurrentVersion = localBuild.ReleaseVersion,
            DisplayVersion = localBuild.DisplayVersion,
            LatestVersion = latestVersion,
            BranchName = localBuild.BranchName,
            CommitSha = localBuild.CommitSha,
            BaseTag = localBuild.BaseTag,
            CommitsSinceTag = localBuild.CommitsSinceTag
        };
    }

    /// <summary>
    /// Gets the release version from the assembly information.
    /// Falls back to "2.0.0-dev" if not available.
    /// </summary>
    private static string GetAssemblyVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;

        if (version != null && (version.Major > 0 || version.Minor > 0 || version.Build > 0))
        {
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }

        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return "2.0.0-dev";
    }

    /// <summary>
    /// Gets the assembly informational version which may include source revision metadata.
    /// </summary>
    private static string GetInformationalVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
    }

    private static LocalBuildInfo GetLocalBuildInfo()
    {
        const string cacheKey = "LingarrLocalBuildInfo";
        if (Cache.TryGetValue(cacheKey, out LocalBuildInfo? cachedBuildInfo) && cachedBuildInfo != null)
        {
            return cachedBuildInfo;
        }

        var buildInfo = CreateLocalBuildInfo();
        Cache.Set(
            cacheKey,
            buildInfo,
            new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMinutes(10))
        );

        return buildInfo;
    }

    private static LocalBuildInfo CreateLocalBuildInfo()
    {
        var releaseVersion = GetAssemblyVersion();
        var informationalVersion = GetInformationalVersion();
        var gitDescribe = TryGetGitOutput("describe --tags --long --always");
        var branchName = TryGetGitOutput("branch --show-current");
        var commitSha = TryGetGitOutput("rev-parse --short HEAD");
        var baseTag = string.Empty;
        int? commitsSinceTag = null;
        var isDevBuild = false;
        var displayVersion = releaseVersion;

        if (!string.IsNullOrWhiteSpace(gitDescribe))
        {
            displayVersion = gitDescribe;

            var describeMatch = GitDescribeRegex.Match(gitDescribe);
            if (describeMatch.Success)
            {
                baseTag = describeMatch.Groups["tag"].Value.TrimStart('v');
                commitSha ??= describeMatch.Groups["sha"].Value;

                if (int.TryParse(describeMatch.Groups["count"].Value, out var parsedCount))
                {
                    commitsSinceTag = parsedCount;
                    isDevBuild = parsedCount > 0;
                }
            }
            else
            {
                isDevBuild = HasDevelopmentMarker(gitDescribe, releaseVersion);
            }
        }

        if (string.IsNullOrWhiteSpace(commitSha))
        {
            commitSha = ExtractCommitSha(informationalVersion);
        }

        if (!isDevBuild)
        {
            isDevBuild = HasDevelopmentMarker(informationalVersion, releaseVersion) ||
                         commitsSinceTag.GetValueOrDefault() > 0;
        }

        if (string.IsNullOrWhiteSpace(gitDescribe) &&
            !string.IsNullOrWhiteSpace(informationalVersion) &&
            HasDevelopmentMarker(informationalVersion, releaseVersion))
        {
            displayVersion = informationalVersion;
        }

        if (string.IsNullOrWhiteSpace(displayVersion) && !string.IsNullOrWhiteSpace(informationalVersion))
        {
            displayVersion = informationalVersion;
        }

        if (string.IsNullOrWhiteSpace(displayVersion))
        {
            displayVersion = releaseVersion;
        }

        if (string.IsNullOrWhiteSpace(baseTag))
        {
            baseTag = releaseVersion;
        }

        return new LocalBuildInfo
        {
            ReleaseVersion = releaseVersion,
            DisplayVersion = displayVersion,
            IsDevBuild = isDevBuild,
            BranchName = branchName,
            CommitSha = commitSha,
            BaseTag = baseTag,
            CommitsSinceTag = commitsSinceTag
        };
    }

    private static bool HasDevelopmentMarker(string? version, string releaseVersion)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var normalizedVersion = version.Trim();
        var normalizedReleaseVersion = releaseVersion.Trim();
        var lowerVersion = normalizedVersion.ToLowerInvariant();

        return lowerVersion.Contains("-dev") ||
               lowerVersion.Contains("-alpha") ||
               lowerVersion.Contains("-beta") ||
               lowerVersion.Contains("-rc") ||
               lowerVersion.Contains("+") ||
               lowerVersion.Contains("commit") ||
               lowerVersion.Contains("sha") ||
               !string.Equals(
                   normalizedVersion.TrimStart('v'),
                   normalizedReleaseVersion.TrimStart('v'),
                   StringComparison.OrdinalIgnoreCase
               );
    }

    private static string? ExtractCommitSha(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return null;
        }

        var metadataSeparatorIndex = informationalVersion.IndexOf('+');
        if (metadataSeparatorIndex < 0 || metadataSeparatorIndex == informationalVersion.Length - 1)
        {
            return null;
        }

        var metadata = informationalVersion[(metadataSeparatorIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        return metadata.Length <= 7 ? metadata : metadata[..7];
    }

    private static string? TryGetGitOutput(string arguments)
    {
        var repositoryRoot = FindRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return null;
        }

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"-C \"{repositoryRoot}\" {arguments}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(3000);

            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output)
                ? output
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindRepositoryRoot()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDirectory != null)
        {
            var gitPath = Path.Combine(currentDirectory.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        return null;
    }

    private static async Task<string> GetLatestVersion()
    {
        const string cacheKey = "GithubLatestRelease";
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

            Cache.Set(
                cacheKey,
                latestVersion,
                new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromHours(24))
            );

            return latestVersion;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Failed to get latest version, returning default application version. Error: {ex.Message}"
            );
            return Number;
        }
    }

    private static bool IsNewVersionAvailable(string latestVersion, string currentVersion)
    {
        latestVersion = latestVersion?.TrimStart('v') ?? string.Empty;
        currentVersion = currentVersion?.TrimStart('v') ?? string.Empty;

        if (string.IsNullOrWhiteSpace(latestVersion) || string.IsNullOrWhiteSpace(currentVersion))
        {
            return false;
        }

        if (SemVersion.TryParse(latestVersion, SemVersionStyles.Any, out var latest) &&
            SemVersion.TryParse(currentVersion, SemVersionStyles.Any, out var current))
        {
            return latest.ComparePrecedenceTo(current) > 0;
        }

        return false;
    }
}
