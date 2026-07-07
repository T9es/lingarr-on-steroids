namespace Lingarr.Server.Models.Sync;

public sealed record MovieRefreshResult(
    int MovieId,
    bool FileChanged,
    string? PreviousFileName,
    string? CurrentFileName,
    DateTime? IndexedAt);
