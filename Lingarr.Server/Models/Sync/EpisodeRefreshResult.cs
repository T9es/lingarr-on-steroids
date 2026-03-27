namespace Lingarr.Server.Models.Sync;

public sealed record EpisodeRefreshResult(
    int EpisodeId,
    bool FileChanged,
    string? PreviousFileName,
    string? CurrentFileName,
    DateTime? IndexedAt);
