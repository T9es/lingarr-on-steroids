using Lingarr.Core.Entities;
using Lingarr.Core.Enum;

namespace Lingarr.Server.Interfaces.Services;

public interface ICustomMediaStateService
{
    Task<TranslationState> UpdateStateAsync(CustomMediaItem item, bool saveChanges = true);
    Task<List<CustomMediaItem>> GetItemsNeedingTranslationAsync(int limit, bool priorityFirst = true);
    Task<int> GetSettingsVersionAsync();
    Task UpdateLastSubtitleCheckAt(int customMediaItemId);
}
