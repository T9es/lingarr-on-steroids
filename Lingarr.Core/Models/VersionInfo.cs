namespace Lingarr.Core.Models;

/// <summary>
/// Contains version information including current version, latest available version,
/// and whether an update is available or this is a development build.
/// </summary>
public class VersionInfo
{
    /// <summary>
    /// Indicates whether a new version is available for update.
    /// Always false for dev builds since they are ahead of releases.
    /// </summary>
    public bool NewVersion { get; set; }

    /// <summary>
    /// Indicates whether this is a development build (not a tagged release).
    /// Dev builds are detected by version suffixes like -dev, -alpha, -beta, etc.
    /// </summary>
    public bool IsDevBuild { get; set; }

    /// <summary>
    /// The current version of the application.
    /// </summary>
    public string? CurrentVersion { get; set; } = string.Empty;

    /// <summary>
    /// The latest released version available on GitHub.
    /// </summary>
    public string? LatestVersion { get; set; } = string.Empty;
}