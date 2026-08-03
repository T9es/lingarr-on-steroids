using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Lingarr.Server.Services.Subtitle;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public sealed class RollbackBackupRecoveryTests : IDisposable
{
    private readonly string _directory;
    private readonly string _finalPath;
    private readonly string _originalContent = "original subtitle content";
    private readonly string _publishedContent = "published subtitle content";
    private readonly string _foreignContent = "foreign subtitle content";

    public RollbackBackupRecoveryTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "lingarr-tests",
            "rollback-backup-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _finalPath = Path.Combine(_directory, "Episode.S01E01.pl.srt");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static string Hash(string content)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }

    private string CreateJobStyleBackup(
        string content,
        int requestId = 7,
        string? expectedPublishedHash = null,
        string? targetPath = null)
    {
        var backupPath = $"{_finalPath}.lingarr-rollback-{Guid.NewGuid():N}.bak";
        File.WriteAllText(backupPath, content);
        RollbackBackupRecovery.WriteManifest(backupPath, new RollbackBackupManifest
        {
            RequestId = requestId,
            TargetPath = targetPath ?? _finalPath,
            OriginalHash = Hash(content),
            ExpectedPublishedHash = expectedPublishedHash
        });
        return backupPath;
    }

    [Fact]
    public void ReconcileSubtitleBackups_WhenFinalMissing_RestoresOriginalFromBackup()
    {
        var backupPath = CreateJobStyleBackup(_originalContent, expectedPublishedHash: Hash(_publishedContent));
        Assert.False(File.Exists(_finalPath));

        RollbackBackupRecovery.ReconcileSubtitleBackups(_finalPath, 7, NullLogger.Instance);

        Assert.True(File.Exists(_finalPath));
        Assert.Equal(_originalContent, File.ReadAllText(_finalPath));
        Assert.False(File.Exists(backupPath));
    }

    [Fact]
    public void ReconcileSubtitleBackups_WhenFinalIsUncommittedPublishedOutput_RestoresOriginal()
    {
        var backupPath = CreateJobStyleBackup(_originalContent, expectedPublishedHash: Hash(_publishedContent));
        File.WriteAllText(_finalPath, _publishedContent);

        RollbackBackupRecovery.ReconcileSubtitleBackups(_finalPath, 7, NullLogger.Instance);

        Assert.Equal(_originalContent, File.ReadAllText(_finalPath));
        Assert.False(File.Exists(backupPath));
    }

    [Fact]
    public void ReconcileSubtitleBackups_WhenFinalMatchesOriginal_DeletesRedundantBackup()
    {
        var backupPath = CreateJobStyleBackup(_originalContent, expectedPublishedHash: Hash(_publishedContent));
        File.WriteAllText(_finalPath, _originalContent);

        RollbackBackupRecovery.ReconcileSubtitleBackups(_finalPath, 7, NullLogger.Instance);

        Assert.Equal(_originalContent, File.ReadAllText(_finalPath));
        Assert.False(File.Exists(backupPath));
        Assert.False(File.Exists($"{backupPath}.meta.json"));
    }

    [Fact]
    public void ReconcileSubtitleBackups_WhenFinalIsForeign_KeepsBackupAndManifest()
    {
        var backupPath = CreateJobStyleBackup(_originalContent, expectedPublishedHash: Hash(_publishedContent));
        File.WriteAllText(_finalPath, _foreignContent);

        RollbackBackupRecovery.ReconcileSubtitleBackups(_finalPath, 7, NullLogger.Instance);

        Assert.Equal(_foreignContent, File.ReadAllText(_finalPath));
        Assert.True(File.Exists(backupPath));
        Assert.True(File.Exists($"{backupPath}.meta.json"));
    }

    [Fact]
    public void ReconcileSubtitleBackups_WhenBackupHasNoManifestAndFinalExists_LeavesBothUntouched()
    {
        var backupPath = $"{_finalPath}.lingarr-rollback-{Guid.NewGuid():N}.bak";
        File.WriteAllText(backupPath, _originalContent);
        File.WriteAllText(_finalPath, _foreignContent);

        RollbackBackupRecovery.ReconcileSubtitleBackups(_finalPath, 7, NullLogger.Instance);

        Assert.Equal(_foreignContent, File.ReadAllText(_finalPath));
        Assert.True(File.Exists(backupPath));
    }

    [Fact]
    public void ReconcileSubtitleBackups_WhenBackupHasNoManifestAndFinalMissing_RestoresBackup()
    {
        var backupPath = $"{_finalPath}.lingarr-rollback-{Guid.NewGuid():N}.bak";
        File.WriteAllText(backupPath, _originalContent);

        RollbackBackupRecovery.ReconcileSubtitleBackups(_finalPath, 7, NullLogger.Instance);

        Assert.True(File.Exists(_finalPath));
        Assert.Equal(_originalContent, File.ReadAllText(_finalPath));
    }

    [Fact]
    public void ReconcileSubtitleBackups_IgnoresBackupWhoseManifestTargetsAnotherFile()
    {
        // Backup name matches the scan pattern for _finalPath, but its manifest
        // protects a different target: it must be left untouched.
        var backupPath = CreateJobStyleBackup(
            _originalContent,
            expectedPublishedHash: Hash(_publishedContent),
            targetPath: Path.Combine(_directory, "Other.S01E01.pl.srt"));
        File.WriteAllText(_finalPath, _publishedContent);

        RollbackBackupRecovery.ReconcileSubtitleBackups(_finalPath, 7, NullLogger.Instance);

        Assert.Equal(_publishedContent, File.ReadAllText(_finalPath));
        Assert.True(File.Exists(backupPath));
    }

    [Fact]
    public void ReconcileSubtitleBackups_ReconcilesCompareServiceStyleSiblingBackups()
    {
        // The completed-edits path names backups {base}.{token}.bak{ext}.
        var backupPath = $"{_finalPath[..^Path.GetExtension(_finalPath).Length]}.completed-compare-abc.bak.srt";
        File.WriteAllText(backupPath, _originalContent);
        RollbackBackupRecovery.WriteManifest(backupPath, new RollbackBackupManifest
        {
            RequestId = 7,
            TargetPath = _finalPath,
            OriginalHash = Hash(_originalContent),
            ExpectedPublishedHash = Hash(_publishedContent)
        });
        File.WriteAllText(_finalPath, _publishedContent);

        RollbackBackupRecovery.ReconcileSubtitleBackups(_finalPath, 7, NullLogger.Instance);

        Assert.Equal(_originalContent, File.ReadAllText(_finalPath));
        Assert.False(File.Exists(backupPath));
    }

    [Fact]
    public void ReconcileRequestEmbeddedBackups_RestoresMediaFromTempBackupOfSameRequest()
    {
        var mediaPath = Path.Combine(_directory, "Episode.S01E01.mkv");
        File.WriteAllText(mediaPath, _publishedContent); // uncommitted merged output
        var backupPath = Path.Combine(Path.GetTempPath(), $"lingarr_normal_embed_{Guid.NewGuid():N}.bak");
        File.WriteAllText(backupPath, _originalContent);
        RollbackBackupRecovery.WriteManifest(backupPath, new RollbackBackupManifest
        {
            RequestId = 42,
            TargetPath = mediaPath,
            OriginalHash = Hash(_originalContent),
            ExpectedPublishedHash = Hash(_publishedContent)
        });

        try
        {
            RollbackBackupRecovery.ReconcileRequestEmbeddedBackups(42, NullLogger.Instance);

            Assert.Equal(_originalContent, File.ReadAllText(mediaPath));
            Assert.False(File.Exists(backupPath));
        }
        finally
        {
            RollbackBackupRecovery.DeleteBackup(backupPath);
        }
    }

    [Fact]
    public void ReconcileRequestEmbeddedBackups_IgnoresTempBackupsOfOtherRequests()
    {
        var mediaPath = Path.Combine(_directory, "Episode.S01E01.mkv");
        File.WriteAllText(mediaPath, _publishedContent);
        var backupPath = Path.Combine(Path.GetTempPath(), $"lingarr_normal_embed_{Guid.NewGuid():N}.bak");
        File.WriteAllText(backupPath, _originalContent);
        RollbackBackupRecovery.WriteManifest(backupPath, new RollbackBackupManifest
        {
            RequestId = 42,
            TargetPath = mediaPath,
            OriginalHash = Hash(_originalContent),
            ExpectedPublishedHash = Hash(_publishedContent)
        });

        try
        {
            RollbackBackupRecovery.ReconcileRequestEmbeddedBackups(43, NullLogger.Instance);

            Assert.Equal(_publishedContent, File.ReadAllText(mediaPath));
            Assert.True(File.Exists(backupPath));
        }
        finally
        {
            RollbackBackupRecovery.DeleteBackup(backupPath);
        }
    }

    [Fact]
    public void ReconcileSubtitleBackups_WhenTargetExistsButIsUnreadable_KeepsBackup()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // File-sharing semantics differ on Linux; this lock-based test is Windows-only
        }

        var backupPath = CreateJobStyleBackup(_originalContent, expectedPublishedHash: Hash(_publishedContent));
        File.WriteAllText(_finalPath, _publishedContent);

        using (new FileStream(_finalPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            RollbackBackupRecovery.ReconcileSubtitleBackups(_finalPath, 7, NullLogger.Instance);
        }

        // The target exists but could not be verified: the backup must survive.
        Assert.Equal(_publishedContent, File.ReadAllText(_finalPath));
        Assert.True(File.Exists(backupPath));
        Assert.True(File.Exists($"{backupPath}.meta.json"));
    }

    [Fact]
    public void ReconcileSubtitleBackups_MatchesManifestTargetCaseAccordingToPlatform()
    {
        var mediaPath = Path.Combine(_directory, "Movie.mkv");
        var caseVariantPath = Path.Combine(_directory, "movie.mkv");
        File.WriteAllText(mediaPath, _publishedContent);
        var backupPath = $"{mediaPath}.lingarr-rollback-{Guid.NewGuid():N}.bak";
        File.WriteAllText(backupPath, _originalContent);
        RollbackBackupRecovery.WriteManifest(backupPath, new RollbackBackupManifest
        {
            RequestId = 7,
            TargetPath = caseVariantPath,
            OriginalHash = Hash(_originalContent),
            ExpectedPublishedHash = Hash(_publishedContent)
        });

        RollbackBackupRecovery.ReconcileSubtitleBackups(mediaPath, 7, NullLogger.Instance);

        if (OperatingSystem.IsWindows())
        {
            // Paths are case-insensitive on Windows: the manifest matches and the
            // uncommitted output is replaced by the original.
            Assert.Equal(_originalContent, File.ReadAllText(mediaPath));
        }
        else
        {
            // Paths are case-sensitive on Linux (TrueNAS): the manifest targets a
            // different file and must be left untouched.
            Assert.Equal(_publishedContent, File.ReadAllText(mediaPath));
            Assert.True(File.Exists(backupPath));
        }
    }

    [Fact]
    public void DeleteBackup_RemovesBackupAndManifest()
    {
        var backupPath = Path.Combine(_directory, "some.bak");
        File.WriteAllText(backupPath, "content");
        RollbackBackupRecovery.WriteManifest(backupPath, new RollbackBackupManifest
        {
            RequestId = 1,
            TargetPath = _finalPath,
            OriginalHash = Hash("content")
        });

        RollbackBackupRecovery.DeleteBackup(backupPath);

        Assert.False(File.Exists(backupPath));
        Assert.False(File.Exists($"{backupPath}.meta.json"));
    }

    [Fact]
    public void UpdateManifest_RewritesExpectedPublishedHash()
    {
        var backupPath = Path.Combine(_directory, "some.bak");
        File.WriteAllText(backupPath, "content");
        RollbackBackupRecovery.WriteManifest(backupPath, new RollbackBackupManifest
        {
            RequestId = 1,
            TargetPath = _finalPath,
            OriginalHash = Hash("content")
        });

        RollbackBackupRecovery.UpdateManifest(
            backupPath,
            manifest => manifest.ExpectedPublishedHash = "expected-hash");

        var json = File.ReadAllText($"{backupPath}.meta.json");
        Assert.Contains("expected-hash", json);
    }
}
