namespace Lingarr.Server.Interfaces.Services.Subtitle;

public interface ITranslationSubtitleRepairService
{
    /// <summary>
    /// Scans Completed translation requests with missing or orphaned
    /// TranslatedSubtitle paths, and attempts to repair them by:
    /// 1. Checking GeneratedSubtitlePaths for existing files
    /// 2. Generating fallback paths from the media file location
    /// 3. Checking for lingarr_merged_* MKV embedding markers
    /// </summary>
    Task<SubtitleRepairSummary> RepairOrphanedRecordsAsync(CancellationToken cancellationToken = default);
}

public class SubtitleRepairSummary
{
    public int Scanned { get; set; }
    public int FixedByExistingFiles { get; set; }
    public int FixedByMkvMarker { get; set; }
    public int SkippedNoMediaPath { get; set; }
    public int Unfixable { get; set; }
    public List<string> Details { get; set; } = [];

    public string Summary =>
        $"Scanned {Scanned} records. " +
        $"Fixed by existing files: {FixedByExistingFiles}, " +
        $"Fixed by MKV marker: {FixedByMkvMarker}, " +
        $"Skipped (no media path): {SkippedNoMediaPath}, " +
        $"Unfixable: {Unfixable}";
}
