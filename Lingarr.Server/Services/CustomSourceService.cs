using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

public class CustomSourceService : ICustomSourceService
{
    private readonly LingarrDbContext _dbContext;
    private readonly IDirectoryService _directoryService;
    private readonly ICustomSourceScannerService _scannerService;

    public CustomSourceService(
        LingarrDbContext dbContext,
        IDirectoryService directoryService,
        ICustomSourceScannerService scannerService)
    {
        _dbContext = dbContext;
        _directoryService = directoryService;
        _scannerService = scannerService;
    }

    public Task<List<CustomSource>> GetSourcesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.CustomSources
            .Include(source => source.Items)
            .OrderBy(source => source.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<CustomSource?> GetSourceAsync(int id, CancellationToken cancellationToken = default)
    {
        return _dbContext.CustomSources
            .Include(source => source.Items)
            .FirstOrDefaultAsync(source => source.Id == id, cancellationToken);
    }

    public async Task<CustomSource> CreateSourceAsync(CustomSource source, CancellationToken cancellationToken = default)
    {
        ValidateSource(source);

        _dbContext.CustomSources.Add(source);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _scannerService.ScanSourceAsync(source.Id, cancellationToken);
        return await GetSourceAsync(source.Id, cancellationToken) ?? source;
    }

    public async Task<CustomSource?> UpdateSourceAsync(int id, CustomSource source, CancellationToken cancellationToken = default)
    {
        ValidateSource(source);

        var existing = await _dbContext.CustomSources.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (existing == null)
        {
            return null;
        }

        existing.Name = source.Name;
        existing.SourceType = source.SourceType;
        existing.RootPath = source.RootPath;
        existing.Recursive = source.Recursive;
        existing.Enabled = source.Enabled;
        existing.IncludeInAutomation = source.IncludeInAutomation;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _scannerService.ScanSourceAsync(existing.Id, cancellationToken);

        return await GetSourceAsync(existing.Id, cancellationToken);
    }

    public async Task<bool> DeleteSourceAsync(int id, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.CustomSources.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (existing == null)
        {
            return false;
        }

        _dbContext.CustomSources.Remove(existing);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<List<CustomMediaItem>> GetItemsAsync(int sourceId, CancellationToken cancellationToken = default)
    {
        return _dbContext.CustomMediaItems
            .Where(item => item.CustomSourceId == sourceId)
            .OrderByDescending(item => item.IsPriority)
            .ThenBy(item => item.SeriesTitle)
            .ThenBy(item => item.SeasonNumber)
            .ThenBy(item => item.EpisodeNumber)
            .ThenBy(item => item.Title)
            .ToListAsync(cancellationToken);
    }

    public Task<CustomMediaItem?> GetItemAsync(int itemId, CancellationToken cancellationToken = default)
    {
        return _dbContext.CustomMediaItems
            .Include(item => item.CustomSource)
            .FirstOrDefaultAsync(item => item.Id == itemId, cancellationToken);
    }

    public async Task<bool> SetItemExcludedAsync(int itemId, bool excluded, CancellationToken cancellationToken = default)
    {
        var item = await _dbContext.CustomMediaItems.FirstOrDefaultAsync(mediaItem => mediaItem.Id == itemId, cancellationToken);
        if (item == null)
        {
            return false;
        }

        item.ExcludeFromTranslation = excluded;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetItemPriorityAsync(int itemId, bool isPriority, CancellationToken cancellationToken = default)
    {
        var item = await _dbContext.CustomMediaItems.FirstOrDefaultAsync(mediaItem => mediaItem.Id == itemId, cancellationToken);
        if (item == null)
        {
            return false;
        }

        item.IsPriority = isPriority;
        item.PriorityDate = isPriority ? DateTime.UtcNow : null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> RescanEnabledSourcesAsync(CancellationToken cancellationToken = default)
    {
        var sourceIds = await _dbContext.CustomSources
            .Where(source => source.Enabled)
            .Select(source => source.Id)
            .ToListAsync(cancellationToken);

        foreach (var sourceId in sourceIds)
        {
            await _scannerService.ScanSourceAsync(sourceId, cancellationToken);
        }

        return sourceIds.Count;
    }

    private void ValidateSource(CustomSource source)
    {
        if (string.IsNullOrWhiteSpace(source.Name))
        {
            throw new ArgumentException("Custom source name is required.");
        }

        if (string.IsNullOrWhiteSpace(source.RootPath))
        {
            throw new ArgumentException("Custom source root path is required.");
        }

        var directory = _directoryService.GetDirectoryInfo(source.RootPath);
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException($"Custom source root '{source.RootPath}' was not found.");
        }
    }
}
