namespace Lingarr.Server.Services.Subtitle;

/// <summary>
/// Resolves the media file on disk when the database stores only an extension-less file name.
/// </summary>
public static class MediaFileResolver
{
    private static readonly string[] MediaExtensions =
        [".mkv", ".mp4", ".avi", ".m4v", ".webm", ".mov", ".wmv"];

    /// <summary>
    /// Resolves a media file from a directory and an extension-less file name.
    /// </summary>
    /// <param name="directory">Directory that contains the media file.</param>
    /// <param name="fileName">Stored file name, with or without a media extension.</param>
    /// <returns>The full path when a matching file exists, otherwise null.</returns>
    public static string? Resolve(string? directory, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var direct = Path.Combine(directory, fileName);
        if (File.Exists(direct))
        {
            return direct;
        }

        if (!Directory.Exists(directory))
        {
            return null;
        }

        var hasMediaExtension = MediaExtensions.Contains(
            Path.GetExtension(fileName),
            StringComparer.OrdinalIgnoreCase);
        var baseName = hasMediaExtension ? Path.GetFileNameWithoutExtension(fileName) : fileName;
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return null;
        }

        foreach (var candidate in Directory.EnumerateFiles(directory))
        {
            if (!string.Equals(
                    Path.GetFileNameWithoutExtension(candidate),
                    baseName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (MediaExtensions.Contains(Path.GetExtension(candidate), StringComparer.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }
}
