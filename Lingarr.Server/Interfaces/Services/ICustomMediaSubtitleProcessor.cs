using Lingarr.Core.Entities;

namespace Lingarr.Server.Interfaces.Services;

public interface ICustomMediaSubtitleProcessor
{
    Task<int> ProcessCustomItemForceAsync(
        CustomMediaItem item,
        bool forceProcess = true,
        bool forceTranslation = true,
        bool forcePriority = false);
}
