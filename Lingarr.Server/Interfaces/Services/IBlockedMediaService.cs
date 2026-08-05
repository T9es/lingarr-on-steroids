using Lingarr.Server.Models.Api;

namespace Lingarr.Server.Interfaces.Services;

public interface IBlockedMediaService
{
    /// <summary>
    /// Retrieves media items currently blocked from translation
    /// (OcrBlocked first, then Stale, then AwaitingSource, each ordered by title).
    /// </summary>
    /// <param name="limit">Maximum number of items to return. Defaults to 200.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of blocked media items.</returns>
    Task<List<BlockedMediaItemResponse>> GetBlockedMediaAsync(
        int limit = 200,
        CancellationToken cancellationToken = default);
}
