namespace Lingarr.Server.Services;

internal static class PathStringHelper
{
    public static string GetFileName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmedPath = path.TrimEnd('/', '\\');
        var separatorIndex = Math.Max(trimmedPath.LastIndexOf('/'), trimmedPath.LastIndexOf('\\'));
        return separatorIndex >= 0 ? trimmedPath[(separatorIndex + 1)..] : trimmedPath;
    }

    public static string? GetDirectoryName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmedPath = path.TrimEnd('/', '\\');
        var separatorIndex = Math.Max(trimmedPath.LastIndexOf('/'), trimmedPath.LastIndexOf('\\'));
        if (separatorIndex < 0)
        {
            return null;
        }

        return separatorIndex == 0 ? trimmedPath[..1] : trimmedPath[..separatorIndex];
    }

    public static string GetFileNameWithoutExtension(string path)
    {
        return Path.GetFileNameWithoutExtension(GetFileName(path));
    }
}
