using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services;

public class DirectoryService : IDirectoryService
{
    private static readonly HashSet<string> BlockedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/proc",
        "/sys",
        "/dev",
        "/etc",
        "/root",
        "/bin",
        "/sbin",
        "/usr",
        "/var",
        "/tmp",
        "/boot",
        "/lib",
        "/lib64",
        "/opt",
        "/srv",
        "/run"
    };

    /// <inheritdoc />
    public DirectoryInfo GetDirectoryInfo(string path)
    {
        var directoryInfo = new DirectoryInfo(path);
        ValidatePath(directoryInfo.FullName);
        return directoryInfo;
    }
    
    /// <inheritdoc />
    public List<DirectoryItem> GetDirectoryContents(string path)
    {
        var directory = GetDirectoryInfo(path);
        var items = new List<DirectoryItem>();

        foreach (var dir in directory.GetDirectories())
        {
            // Skip blocked subdirectories to prevent listing them even if they are children
            if (IsPathBlocked(dir.FullName))
            {
                continue;
            }

            // Skip hidden directories (starting with .) to prevent information leakage
            if (dir.Name.StartsWith('.'))
            {
                continue;
            }

            items.Add(new DirectoryItem
            {
                Name = dir.Name,
                FullPath = dir.FullName
            });
        }

        return items.OrderBy(i => i.Name).ToList();
    }

    private void ValidatePath(string fullPath)
    {
        if (IsPathBlocked(fullPath))
        {
            throw new UnauthorizedAccessException($"Access to {fullPath} is restricted by security policy.");
        }
    }

    private bool IsPathBlocked(string fullPath)
    {
        // Check if the path is exactly one of the blocked paths
        if (BlockedPaths.Contains(fullPath))
        {
            return true;
        }

        // Check if the path is a subdirectory of a blocked path (e.g. /etc/nginx)
        foreach (var blockedPath in BlockedPaths)
        {
            if (fullPath.StartsWith(blockedPath + Path.DirectorySeparatorChar) ||
                fullPath.StartsWith(blockedPath + Path.AltDirectorySeparatorChar))
            {
                return true;
            }
        }

        return false;
    }
}
