using Lingarr.Core.Entities;

namespace Lingarr.Server.Interfaces.Services;

public interface ICustomSourceService
{
    Task<List<CustomSource>> GetSourcesAsync(CancellationToken cancellationToken = default);
    Task<CustomSource?> GetSourceAsync(int id, CancellationToken cancellationToken = default);
    Task<CustomSource> CreateSourceAsync(CustomSource source, CancellationToken cancellationToken = default);
    Task<CustomSource?> UpdateSourceAsync(int id, CustomSource source, CancellationToken cancellationToken = default);
    Task<bool> DeleteSourceAsync(int id, CancellationToken cancellationToken = default);
    Task<List<CustomMediaItem>> GetItemsAsync(int sourceId, CancellationToken cancellationToken = default);
    Task<CustomMediaItem?> GetItemAsync(int itemId, CancellationToken cancellationToken = default);
    Task<bool> SetItemExcludedAsync(int itemId, bool excluded, CancellationToken cancellationToken = default);
    Task<bool> SetItemPriorityAsync(int itemId, bool isPriority, CancellationToken cancellationToken = default);
    Task<int> RescanEnabledSourcesAsync(CancellationToken cancellationToken = default);
}
