using System.Text.RegularExpressions;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.CustomSources;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

public class CustomSourceScannerService : ICustomSourceScannerService
{
    private static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv",
        ".mp4",
        ".avi",
        ".mov",
        ".wmv",
        ".m4v",
        ".webm"
    };

    private readonly LingarrDbContext _dbContext;
    private readonly IDirectoryService _directoryService;
    private readonly ISettingService _settingService;
    private readonly ILogger<CustomSourceScannerService> _logger;

    public CustomSourceScannerService(
        LingarrDbContext dbContext,
        IDirectoryService directoryService,
        ISettingService settingService,
        ILogger<CustomSourceScannerService> logger)
    {
        _dbContext = dbContext;
        _directoryService = directoryService;
        _settingService = settingService;
        _logger = logger;
    }

    public async Task<CustomSourceScanResult> ScanSourceAsync(int customSourceId, CancellationToken cancellationToken = default)
    {
        var source = await _dbContext.CustomSources
            .Include(customSource => customSource.Items)
            .FirstOrDefaultAsync(customSource => customSource.Id == customSourceId, cancellationToken);

        if (source == null)
        {
            throw new InvalidOperationException($"Custom source {customSourceId} was not found.");
        }

        try
        {
            var root = _directoryService.GetDirectoryInfo(source.RootPath);
            if (!root.Exists)
            {
                throw new DirectoryNotFoundException($"Custom source root '{source.RootPath}' was not found.");
            }

            var currentVersion = await GetSettingsVersionAsync();
            var now = DateTime.UtcNow;
            var enumerationOptions = new EnumerationOptions
            {
                RecurseSubdirectories = source.Recursive,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false
            };

            var discoveredFiles = root
                .EnumerateFiles("*", enumerationOptions)
                .Where(file => SupportedVideoExtensions.Contains(file.Extension))
                .ToList();

            var existingByPath = source.Items.ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var indexedCount = 0;

            foreach (var file in discoveredFiles)
            {
                var fullPath = file.FullName;
                var relativePath = Path.GetRelativePath(source.RootPath, fullPath);

                seenPaths.Add(fullPath);

                if (!existingByPath.TryGetValue(fullPath, out var item))
                {
                    item = new CustomMediaItem
                    {
                        CustomSourceId = source.Id,
                        CustomSource = source,
                        ItemKind = source.SourceType == CustomSourceType.MovieRoot
                            ? CustomMediaItemKind.Movie
                            : CustomMediaItemKind.Episode,
                        Title = BuildDisplayTitle(file, relativePath, source.SourceType),
                        FileName = file.Name,
                        Path = fullPath,
                        RelativePath = relativePath,
                        DateAdded = file.CreationTimeUtc,
                        IndexedAt = now,
                        StateSettingsVersion = currentVersion
                    };

                    ApplyShowMetadata(item, relativePath, file.Name, source.SourceType);
                    _dbContext.CustomMediaItems.Add(item);
                }
                else
                {
                    item.ItemKind = source.SourceType == CustomSourceType.MovieRoot
                        ? CustomMediaItemKind.Movie
                        : CustomMediaItemKind.Episode;
                    item.Title = BuildDisplayTitle(file, relativePath, source.SourceType);
                    item.FileName = file.Name;
                    item.RelativePath = relativePath;
                    item.DateAdded ??= file.CreationTimeUtc;
                    item.IndexedAt = now;
                    item.StateSettingsVersion = currentVersion;
                    ApplyShowMetadata(item, relativePath, file.Name, source.SourceType);
                }

                indexedCount++;
            }

            var removedItems = source.Items
                .Where(item => !seenPaths.Contains(item.Path))
                .ToList();

            if (removedItems.Count > 0)
            {
                _dbContext.CustomMediaItems.RemoveRange(removedItems);
            }

            source.LastScannedAt = now;
            source.LastScanResult = $"Indexed {indexedCount} item(s), removed {removedItems.Count}.";
            source.LastScanError = null;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new CustomSourceScanResult
            {
                IndexedCount = indexedCount,
                RemovedCount = removedItems.Count
            };
        }
        catch (Exception ex)
        {
            source.LastScannedAt = DateTime.UtcNow;
            source.LastScanError = ex.Message;
            source.LastScanResult = "failed";
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(ex, "Failed to scan custom source {SourceId} at {RootPath}", source.Id, source.RootPath);
            throw;
        }
    }

    private async Task<int> GetSettingsVersionAsync()
    {
        var versionString = await _settingService.GetSetting(SettingKeys.Translation.LanguageSettingsVersion);
        return int.TryParse(versionString, out var version) ? version : 1;
    }

    private static string BuildDisplayTitle(FileInfo file, string relativePath, CustomSourceType sourceType)
    {
        var baseName = Path.GetFileNameWithoutExtension(file.Name)
            .Replace('.', ' ')
            .Replace('_', ' ')
            .Trim();

        if (sourceType == CustomSourceType.MovieRoot)
        {
            return baseName;
        }

        var metadata = TryParseEpisodeMetadata(relativePath, file.Name);
        if (metadata.SeriesTitle == null)
        {
            return baseName;
        }

        if (metadata.SeasonNumber.HasValue && metadata.EpisodeNumber.HasValue)
        {
            return $"{metadata.SeriesTitle} - S{metadata.SeasonNumber.Value:D2}E{metadata.EpisodeNumber.Value:D2}";
        }

        return metadata.SeriesTitle;
    }

    private static void ApplyShowMetadata(CustomMediaItem item, string relativePath, string fileName, CustomSourceType sourceType)
    {
        if (sourceType != CustomSourceType.ShowRoot)
        {
            item.SeriesTitle = null;
            item.SeasonNumber = null;
            item.EpisodeNumber = null;
            return;
        }

        var metadata = TryParseEpisodeMetadata(relativePath, fileName);
        item.SeriesTitle = metadata.SeriesTitle;
        item.SeasonNumber = metadata.SeasonNumber;
        item.EpisodeNumber = metadata.EpisodeNumber;
    }

    private static (string? SeriesTitle, int? SeasonNumber, int? EpisodeNumber) TryParseEpisodeMetadata(string relativePath, string fileName)
    {
        var normalizedRelativePath = relativePath.Replace('\\', '/');
        var parts = normalizedRelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var fileBaseName = Path.GetFileNameWithoutExtension(fileName);

        string? seriesTitle = parts.Length > 1
            ? parts[0].Replace('.', ' ').Replace('_', ' ').Trim()
            : null;

        int? seasonNumber = null;
        int? episodeNumber = null;

        if (parts.Length > 1)
        {
            var seasonMatch = Regex.Match(parts[1], @"[Ss]eason[\s._-]*(\d{1,2})|[Ss](\d{1,2})");
            if (seasonMatch.Success)
            {
                var raw = seasonMatch.Groups[1].Success ? seasonMatch.Groups[1].Value : seasonMatch.Groups[2].Value;
                if (int.TryParse(raw, out var parsedSeason))
                {
                    seasonNumber = parsedSeason;
                }
            }
        }

        var episodeMatch = Regex.Match(fileBaseName, @"[Ss](\d{1,2})[Ee](\d{1,2})");
        if (episodeMatch.Success)
        {
            if (int.TryParse(episodeMatch.Groups[1].Value, out var parsedSeason))
            {
                seasonNumber ??= parsedSeason;
            }

            if (int.TryParse(episodeMatch.Groups[2].Value, out var parsedEpisode))
            {
                episodeNumber = parsedEpisode;
            }
        }

        return (seriesTitle, seasonNumber, episodeNumber);
    }
}
