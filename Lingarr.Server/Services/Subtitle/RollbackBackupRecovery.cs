using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Lingarr.Server.Services.Subtitle;

/// <summary>
/// Metadata sidecar written next to every rollback backup created during staged
/// subtitle / MKV publication. It records which file the backup protects, which
/// request created it, and the hashes needed to decide, after a crash, whether
/// the current file is the crashed attempt's uncommitted output, the untouched
/// original, or a foreign file that must not be touched.
/// </summary>
internal sealed class RollbackBackupManifest
{
    public int RequestId { get; set; }

    /// <summary>The final subtitle path or media path the backup protects.</summary>
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>Hash of the file before publication (the content the backup holds).</summary>
    public string? OriginalHash { get; set; }

    /// <summary>Hash of the file this publication wrote (null until publication finished).</summary>
    public string? ExpectedPublishedHash { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Crash recovery for rollback backups created during staged subtitle/MKV publication.
///
/// Backups are only ever deleted on the success path (after the database commit).
/// If the process crashes between publishing files and committing to the database,
/// the backup outlives its transaction and must be reconciled before the next
/// attempt publishes to the same path:
///   - final file missing            -> restore the backup (it is the only copy of the original)
///   - final file hash == expected   -> final is the crashed attempt's uncommitted output: restore
///   - final file hash == original   -> media/output untouched: backup is redundant, delete it
///   - anything else                 -> foreign writer: keep backup + manifest untouched
/// Backups without a manifest (pre-existing orphans) are only restored when the final
/// file is missing; otherwise they are left alone.
/// </summary>
internal static class RollbackBackupRecovery
{
    private const string ManifestSuffix = ".meta.json";
    private const string JobRollbackPrefix = ".lingarr-rollback-";
    private const string JobRollbackSuffix = ".bak";
    private const string JobEmbeddedBackupPattern = "lingarr_normal_embed_*.bak";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Path identity is case-insensitive on Windows but case-sensitive on Linux/macOS
    // (TrueNAS deployments run Linux; a case-blind match could pair a manifest with
    // the wrong file there).
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static readonly IEqualityComparer<string> PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Writes (atomically) the manifest sidecar for a backup.</summary>
    public static void WriteManifest(string backupPath, RollbackBackupManifest manifest)
    {
        var manifestPath = GetManifestPath(backupPath);
        var tempPath = $"{manifestPath}.tmp-{Guid.NewGuid():N}";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(manifest, JsonOptions));
        try
        {
            File.Move(tempPath, manifestPath, overwrite: true);
        }
        catch
        {
            File.Delete(tempPath);
            throw;
        }
    }

    /// <summary>Rewrites the manifest for a backup after its publication state changed.</summary>
    public static void UpdateManifest(string backupPath, Action<RollbackBackupManifest> update)
    {
        var manifest = LoadManifest(backupPath);
        if (manifest == null)
        {
            return;
        }

        update(manifest);
        WriteManifest(backupPath, manifest);
    }

    /// <summary>Deletes a rollback backup together with its manifest sidecar.</summary>
    public static void DeleteBackup(string? backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            return;
        }

        DeleteFileIfExists(backupPath);
        DeleteFileIfExists(GetManifestPath(backupPath));
    }

    /// <summary>Deletes only the manifest sidecar (used after a backup was moved back into place).</summary>
    public static void DeleteManifest(string backupPath)
    {
        DeleteFileIfExists(GetManifestPath(backupPath));
    }

    /// <summary>
    /// Reconciles stale rollback backups that protect <paramref name="finalPath"/>
    /// (subtitle outputs). Called right before publishing to that path.
    /// </summary>
    public static void ReconcileSubtitleBackups(string finalPath, int requestId, ILogger logger)
    {
        var directory = Path.GetDirectoryName(finalPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        var fileName = Path.GetFileName(finalPath);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(finalPath);
        var extension = Path.GetExtension(finalPath);

        var candidates = new List<(string BackupPath, bool AllowLegacyRestore)>();
        // The job-style rollback naming ({file}.lingarr-rollback-{guid}.bak) is unique to
        // Lingarr, so manifest-less orphans can be restored safely. The completed-edits
        // naming ({base}.{token}.bak{ext}) is also matched by ordinary user files, so
        // manifest-less candidates found there are never touched.
        CollectMatchingFiles(directory, $"{fileName}{JobRollbackPrefix}*{JobRollbackSuffix}", candidates, allowLegacyRestore: true);
        CollectMatchingFiles(directory, $"{fileNameWithoutExtension}.*{JobRollbackSuffix}{extension}", candidates, allowLegacyRestore: false);

        ReconcileCandidates(finalPath, candidates, logger);
    }

    /// <summary>
    /// Reconciles stale embedded (MKV) rollback backups protecting <paramref name="mediaPath"/>.
    /// Covers both backup locations: the job's temp-directory backups and the
    /// completed-edits sibling backups.
    /// </summary>
    public static void ReconcileEmbeddedBackups(string mediaPath, int requestId, ILogger logger)
    {
        var tempDirectory = Path.GetTempPath();
        if (Directory.Exists(tempDirectory))
        {
            var tempCandidates = new List<(string BackupPath, bool AllowLegacyRestore)>();
            CollectMatchingFiles(tempDirectory, JobEmbeddedBackupPattern, tempCandidates, allowLegacyRestore: true);
            ReconcileCandidates(mediaPath, tempCandidates, logger);
        }

        var directory = Path.GetDirectoryName(mediaPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(mediaPath);
        var extension = Path.GetExtension(mediaPath);
        var siblingCandidates = new List<(string BackupPath, bool AllowLegacyRestore)>();
        CollectMatchingFiles(directory, $"{fileNameWithoutExtension}.*{JobRollbackSuffix}{extension}", siblingCandidates, allowLegacyRestore: false);
        ReconcileCandidates(mediaPath, siblingCandidates, logger);
    }

    /// <summary>
    /// Reconciles temp-directory embedded backups belonging to a specific request.
    /// Used early in the job, before subtitle extraction, so a retry never reads
    /// from a media container that a crashed attempt already modified.
    /// </summary>
    public static void ReconcileRequestEmbeddedBackups(int requestId, ILogger logger)
    {
        var tempDirectory = Path.GetTempPath();
        if (!Directory.Exists(tempDirectory))
        {
            return;
        }

        var candidates = new List<(string BackupPath, bool AllowLegacyRestore)>();
        CollectMatchingFiles(tempDirectory, JobEmbeddedBackupPattern, candidates, allowLegacyRestore: true);
        foreach (var candidate in candidates)
        {
            var manifest = LoadManifest(candidate.BackupPath);
            if (manifest == null || manifest.RequestId != requestId)
            {
                continue;
            }

            ApplyManifestRules(manifest.TargetPath, candidate.BackupPath, manifest, candidate.AllowLegacyRestore, logger);
        }
    }

    private static void ReconcileCandidates(
        string targetPath,
        List<(string BackupPath, bool AllowLegacyRestore)> candidates,
        ILogger logger)
    {
        if (candidates.Count == 0)
        {
            return;
        }

        var normalizedTarget = NormalizePath(targetPath);
        var manifests = new List<(string BackupPath, bool AllowLegacyRestore, RollbackBackupManifest? Manifest, DateTime CreatedAtUtc)>();
        foreach (var candidate in candidates.DistinctBy(item => item.BackupPath, PathComparer))
        {
            var manifest = LoadManifest(candidate.BackupPath);
            if (manifest != null &&
                !string.Equals(
                    NormalizePath(manifest.TargetPath),
                    normalizedTarget,
                    PathComparison))
            {
                continue;
            }

            manifests.Add((candidate.BackupPath, candidate.AllowLegacyRestore, manifest, manifest?.CreatedAtUtc ?? DateTime.MinValue));
        }

        foreach (var item in manifests.OrderBy(item => item.CreatedAtUtc))
        {
            ApplyManifestRules(targetPath, item.BackupPath, item.Manifest, item.AllowLegacyRestore, logger);
        }
    }

    private static void ApplyManifestRules(
        string targetPath,
        string backupPath,
        RollbackBackupManifest? manifest,
        bool allowLegacyRestore,
        ILogger logger)
    {
        if (!File.Exists(backupPath))
        {
            return;
        }

        var targetExists = File.Exists(targetPath);
        var currentHash = targetExists ? TryComputeHash(targetPath) : null;
        if (targetExists && currentHash == null)
        {
            // The target exists but could not be read (locked, permissions, transient
            // IO error). Never restore over a file we could not verify.
            logger.LogWarning(
                "Keeping rollback backup {BackupPath} for {TargetPath}: the current file exists but could not be verified",
                backupPath,
                targetPath);
            return;
        }

        if (manifest == null)
        {
            if (!allowLegacyRestore)
            {
                // The naming pattern is not uniquely attributable to Lingarr (it also
                // matches ordinary user files), so a manifest-less backup is never
                // moved into place automatically.
                logger.LogDebug(
                    "Leaving orphaned rollback backup {BackupPath} untouched: it has no manifest and its naming is not uniquely attributable to Lingarr",
                    backupPath);
                return;
            }

            // Pre-manifest orphan: only safe to restore when the final file is missing
            // (the backup is then the only copy of the original).
            if (currentHash == null)
            {
                TryRestore(targetPath, backupPath, logger);
            }
            else
            {
                logger.LogDebug(
                    "Leaving orphaned rollback backup {BackupPath} untouched: cannot verify ownership of {TargetPath}",
                    backupPath,
                    targetPath);
            }

            return;
        }

        if (currentHash == null)
        {
            TryRestore(targetPath, backupPath, logger);
            return;
        }

        if (manifest.OriginalHash != null &&
            string.Equals(currentHash, manifest.OriginalHash, StringComparison.Ordinal))
        {
            // The current file is already the original: the backup is redundant.
            // Checked before the published-hash rule so that byte-identical output
            // (e.g. source-preserved positions) deletes instead of restoring.
            logger.LogDebug(
                "Deleting redundant rollback backup {BackupPath} for {TargetPath} (file matches original)",
                backupPath,
                targetPath);
            DeleteBackup(backupPath);
            return;
        }

        if (manifest.ExpectedPublishedHash != null &&
            string.Equals(currentHash, manifest.ExpectedPublishedHash, StringComparison.Ordinal))
        {
            // The current file is the crashed attempt's uncommitted output.
            logger.LogWarning(
                "Restoring pre-publication file for {TargetPath} from rollback backup {BackupPath} (uncommitted output from interrupted attempt {RequestId})",
                targetPath,
                backupPath,
                manifest.RequestId);
            DeleteFileIfExists(targetPath);
            TryRestore(targetPath, backupPath, logger);
            return;
        }

        if (manifest.ExpectedPublishedHash == null)
        {
            // The published hash was never recorded (crash between embed success and the
            // manifest update). The target is modified but not provably foreign; restore
            // the original, matching the runtime rollback semantics for this state.
            logger.LogWarning(
                "Restoring pre-publication file for {TargetPath} from rollback backup {BackupPath} (interrupted attempt {RequestId} left an unrecorded modification)",
                targetPath,
                backupPath,
                manifest.RequestId);
            DeleteFileIfExists(targetPath);
            TryRestore(targetPath, backupPath, logger);
            return;
        }

        logger.LogWarning(
            "Keeping rollback backup {BackupPath} for {TargetPath}: current file was not produced by interrupted attempt {RequestId} and differs from the original",
            backupPath,
            targetPath,
            manifest.RequestId);
    }

    private static void TryRestore(string targetPath, string backupPath, ILogger logger)
    {
        try
        {
            EnsureParentDirectory(targetPath);
            File.Move(backupPath, targetPath, overwrite: true);
            DeleteManifest(backupPath);
            logger.LogWarning(
                "Restored {TargetPath} from rollback backup {BackupPath}",
                targetPath,
                backupPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to restore {TargetPath} from rollback backup {BackupPath}",
                targetPath,
                backupPath);
        }
    }

    private static RollbackBackupManifest? LoadManifest(string backupPath)
    {
        var manifestPath = GetManifestPath(backupPath);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RollbackBackupManifest>(
                File.ReadAllText(manifestPath),
                JsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string GetManifestPath(string backupPath)
    {
        return $"{backupPath}{ManifestSuffix}";
    }

    private static void CollectMatchingFiles(
        string directory,
        string pattern,
        List<(string BackupPath, bool AllowLegacyRestore)> results,
        bool allowLegacyRestore)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, pattern))
            {
                if (!file.EndsWith(ManifestSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add((file, allowLegacyRestore));
                }
            }
        }
        catch (Exception)
        {
            // Directory enumeration can race with concurrent deletion; never fail the caller.
        }
    }

    private static string? TryComputeHash(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string NormalizePath(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Deletion races with other processes are tolerated; leftovers are reconciled later.
        }
    }
}
