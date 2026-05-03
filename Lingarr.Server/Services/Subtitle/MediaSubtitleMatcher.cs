using System.Text.Json;
using Lingarr.Core.Entities;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Subtitle;

public static class MediaSubtitleMatcher
{
    public static HashSet<string> ExtractGeneratedPaths(IEnumerable<TranslationRequest> requests)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var request in requests)
        {
            if (string.IsNullOrWhiteSpace(request.GeneratedSubtitlePaths))
            {
                continue;
            }

            try
            {
                foreach (var path in JsonSerializer.Deserialize<List<string>>(request.GeneratedSubtitlePaths) ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        paths.Add(path);
                    }
                }
            }
            catch (JsonException)
            {
                foreach (var path in request.GeneratedSubtitlePaths.Split(
                             [';', '|', '\n', '\r'],
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    paths.Add(path);
                }
            }
        }

        return paths;
    }

    public static List<Subtitles> FilterMatchingSubtitles(
        string? mediaFileName,
        IEnumerable<Subtitles> subtitles,
        IEnumerable<string>? knownGeneratedPaths = null)
    {
        if (string.IsNullOrWhiteSpace(mediaFileName))
        {
            return [];
        }

        var mediaNameNoExt = Path.GetFileNameWithoutExtension(mediaFileName);
        var knownPaths = knownGeneratedPaths == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : knownGeneratedPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return subtitles
            .Where(subtitle =>
                subtitle.FileName.StartsWith(mediaFileName + ".", StringComparison.OrdinalIgnoreCase)
                || subtitle.FileName.Equals(mediaFileName, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(mediaNameNoExt)
                    && subtitle.FileName.StartsWith(mediaNameNoExt + ".", StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(subtitle.Path)
                    && knownPaths.Contains(NormalizePath(subtitle.Path))))
            .GroupBy(subtitle => NormalizePath(subtitle.Path), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path).Trim();
        }
        catch
        {
            return path.Trim();
        }
    }
}
