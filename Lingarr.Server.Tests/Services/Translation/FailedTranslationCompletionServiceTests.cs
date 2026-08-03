using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Translation;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Translation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class FailedTranslationCompletionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LingarrDbContext _dbContext;
    private readonly string _tempDirectory;

    public FailedTranslationCompletionServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new LingarrDbContext(options);
        _dbContext.Database.EnsureCreated();

        _tempDirectory = Path.Combine(Path.GetTempPath(), "lingarr-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task CompleteAsync_WhenAssRequestRequiresAssAndSrt_WritesAndRecordsBothOutputs()
    {
        var sourcePath = Path.Combine(_tempDirectory, "episode.en.ass");
        await File.WriteAllTextAsync(sourcePath, CreateAssSubtitle());

        var request = new TranslationRequest
        {
            Title = "Episode",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            SourceSubtitleFormat = ".ass",
            SubtitleOutputMode = "both",
            RequiredOutputFormats = ".ass,.srt",
            SourceSnapshotFingerprint = BuildFallbackFingerprint(sourcePath, "en", "pl", ".ass"),
            MediaType = MediaType.Episode,
            Status = TranslationStatus.Failed,
            WorkloadKind = TranslationWorkloadKind.CustomSource
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var checkpoint = new TranslationCheckpoint
        {
            TranslationRequestId = request.Id,
            SourceFingerprint = BuildFallbackFingerprint(sourcePath, "en", "pl", ".ass"),
            Translations =
            {
                [1] = "Przetlumaczony tekst",
                [3] = "Echowane zdanie"
            }
        };

        var checkpointService = new Mock<ITranslationCheckpointService>();
        checkpointService
            .Setup(service => service.LoadByRequestIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkpoint);
        checkpointService
            .Setup(service => service.DeleteAsync(
                request.Id,
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(
            sourcePath,
            checkpointService.Object,
            new Dictionary<string, string>
            {
                [SettingKeys.Translation.UseSubtitleTagging] = "true",
                [SettingKeys.Translation.RemoveLanguageTag] = "false",
                [SettingKeys.Translation.SubtitleTag] = "-ai-",
                [SettingKeys.Translation.SubtitleTagShort] = "-ai-",
                [SettingKeys.Translation.StripSubtitleFormatting] = "false",
                [SettingKeys.Translation.SubtitleOutputMode] = "both"
            });

        var result = await service.CompleteAsync(
            request,
            new Dictionary<int, string>(),
            new HashSet<int> { 3 },
            "Auto-completed test request.",
            CancellationToken.None);

        Assert.True(result.Completed);
        var updatedRequest = await _dbContext.TranslationRequests.FindAsync(request.Id);
        Assert.NotNull(updatedRequest);
        Assert.Equal(TranslationStatus.Completed, updatedRequest!.Status);
        Assert.Equal(".ass,.srt", updatedRequest.GeneratedOutputFormats);
        Assert.Equal(".ass,.srt", updatedRequest.RequiredOutputFormats);

        var generatedPaths = JsonSerializer.Deserialize<List<string>>(updatedRequest.GeneratedSubtitlePaths!);
        Assert.NotNull(generatedPaths);
        Assert.Equal(2, generatedPaths!.Count);
        Assert.Contains(generatedPaths, path => Path.GetExtension(path).Equals(".ass", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(generatedPaths, path => Path.GetExtension(path).Equals(".srt", StringComparison.OrdinalIgnoreCase));
        Assert.All(generatedPaths, path => Assert.True(File.Exists(path), $"Expected output file to exist: {path}"));

        var srtPath = generatedPaths.Single(path =>
            Path.GetExtension(path).Equals(".srt", StringComparison.OrdinalIgnoreCase));
        var srtContent = await File.ReadAllTextAsync(srtPath);
        var assPath = generatedPaths.Single(path =>
            Path.GetExtension(path).Equals(".ass", StringComparison.OrdinalIgnoreCase));
        var assContent = await File.ReadAllTextAsync(assPath);

        Assert.Contains("Przetlumaczony tekst", srtContent);
        Assert.Contains("Echowane zdanie", srtContent);
        Assert.DoesNotContain("Echo source text", srtContent);
        Assert.DoesNotContain(@"\p1", srtContent);
        Assert.DoesNotContain(@"{\an8}", srtContent);
        Assert.DoesNotContain(@"{\k20}", srtContent);
        Assert.DoesNotContain("m 0 0 l 10 10", srtContent);
        Assert.Contains(@"{\an8}{\t(0,500,\fs30)}{\k20}Przetlumaczony{\k30} tekst", assContent);
        Assert.Equal(updatedRequest.TranslatedSubtitle, result.OutputPath);
        checkpointService.Verify(
            service => service.DeleteAsync(
                request.Id,
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_WhenRequestIsAlreadyCompleted_PreservesCompletedBehavior()
    {
        var existingOutputPath = Path.Combine(_tempDirectory, "episode.pl.ass");
        var request = new TranslationRequest
        {
            Title = "Already completed episode",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = Path.Combine(_tempDirectory, "missing.en.ass"),
            TranslatedSubtitle = existingOutputPath,
            SourceSubtitleFormat = ".ass",
            Status = TranslationStatus.Completed,
            MediaType = MediaType.Episode,
            WorkloadKind = TranslationWorkloadKind.CustomSource
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var checkpointService = new Mock<ITranslationCheckpointService>();
        var service = CreateService(
            request.SubtitleToTranslate!,
            checkpointService.Object,
            new Dictionary<string, string>());

        var result = await service.CompleteAsync(
            request,
            new Dictionary<int, string>(),
            new HashSet<int>(),
            "Should not be written.",
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.True(result.AlreadyCompleted);
        Assert.Equal(existingOutputPath, result.OutputPath);
        checkpointService.Verify(
            checkpoint => checkpoint.LoadByRequestIdAsync(request.Id, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_WhenCheckpointHasStaleAndInvalidEntries_PreservesValidCachedTranslations()
    {
        var sourcePath = Path.Combine(_tempDirectory, "checkpoint-validation.en.srt");
        await File.WriteAllTextAsync(sourcePath, CreateSrtSubtitle(
            "Valid source line",
            "Echo source line",
            "Third source line",
            "[Music]"));

        var request = new TranslationRequest
        {
            Title = "Checkpoint validation episode",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            SourceSubtitleFormat = ".srt",
            SubtitleOutputMode = "srt",
            RequiredOutputFormats = ".srt",
            SourceSnapshotFingerprint = BuildFallbackFingerprint(sourcePath, "en", "pl", ".srt"),
            MediaType = MediaType.Episode,
            Status = TranslationStatus.Failed,
            WorkloadKind = TranslationWorkloadKind.CustomSource
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var checkpoint = new TranslationCheckpoint
        {
            TranslationRequestId = request.Id,
            SourceFingerprint = BuildFallbackFingerprint(sourcePath, "en", "pl", ".srt"),
            Translations = new Dictionary<int, string>
            {
                [1] = "Poprawne tłumaczenie",
                [2] = "Echo source line",
                [3] = "これは日本語です",
                [4] = "[Music]",
                [99] = "Stale position translation",
                [100] = ""
            }
        };
        TranslationCheckpoint? savedCheckpoint = null;
        var checkpointService = new Mock<ITranslationCheckpointService>();
        checkpointService
            .Setup(service => service.LoadByRequestIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkpoint);
         checkpointService
             .Setup(service => service.SaveCheckpointAsync(
                 It.IsAny<TranslationCheckpoint>(),
                 It.IsAny<CancellationToken>()))
             .Callback<TranslationCheckpoint, CancellationToken>((saved, _) => savedCheckpoint = saved)
             .Returns(Task.CompletedTask);
         checkpointService
             .Setup(service => service.SaveCheckpointAsync(
                 It.IsAny<TranslationCheckpoint>(),
                 It.IsAny<CancellationToken>(),
                 It.IsAny<string?>()))
             .Callback<TranslationCheckpoint, CancellationToken, string?>((saved, _, _) => savedCheckpoint = saved)
             .Returns(Task.CompletedTask);
        checkpointService
            .Setup(service => service.DeleteAsync(
                request.Id,
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(
            sourcePath,
            checkpointService.Object,
            new Dictionary<string, string>
            {
                [SettingKeys.Translation.RemoveLanguageTag] = "false",
                [SettingKeys.Translation.StripSubtitleFormatting] = "false",
                [SettingKeys.Translation.SubtitleOutputMode] = "srt"
            });

        var result = await service.CompleteAsync(
            request,
            new Dictionary<int, string>(),
            new HashSet<int>(),
            "Accept validated checkpoint.",
            CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Contains("Untranslated ordinary subtitle", result.SkippedReason);
        Assert.NotNull(savedCheckpoint);
        Assert.Equal("Poprawne tłumaczenie", savedCheckpoint!.Translations[1]);
        Assert.Equal("[Music]", savedCheckpoint.Translations[4]);
        Assert.DoesNotContain(2, savedCheckpoint.Translations.Keys);
        Assert.DoesNotContain(3, savedCheckpoint.Translations.Keys);
        Assert.DoesNotContain(99, savedCheckpoint.Translations.Keys);
        Assert.DoesNotContain(100, savedCheckpoint.Translations.Keys);

        var output = savedCheckpoint.Translations[1];
        Assert.Contains("Poprawne tłumaczenie", output);
        Assert.DoesNotContain("これは日本語です", output);
        checkpointService.Verify(
            service => service.SaveCheckpointAsync(
                It.IsAny<TranslationCheckpoint>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()),
            Times.Once);
        checkpointService.Verify(
            service => service.DeleteAsync(
                request.Id,
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_WhenManualAssEditIsProvided_PreservesSourceFormattingAroundAuthoritativeText()
    {
        var sourcePath = Path.Combine(_tempDirectory, "manual-edit.en.ass");
        await File.WriteAllTextAsync(sourcePath, CreateAssSubtitle());
        var fingerprint = BuildFallbackFingerprint(sourcePath, "en", "pl", ".ass");

        var request = new TranslationRequest
        {
            Title = "Manual ASS edit episode",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            SourceSubtitleFormat = ".ass",
            SubtitleOutputMode = "ass",
            RequiredOutputFormats = ".ass",
            SourceSnapshotFingerprint = fingerprint,
            MediaType = MediaType.Episode,
            Status = TranslationStatus.Failed,
            WorkloadKind = TranslationWorkloadKind.CustomSource
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var checkpointService = new Mock<ITranslationCheckpointService>();
        checkpointService
            .Setup(service => service.LoadByRequestIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranslationCheckpoint
            {
                TranslationRequestId = request.Id,
                SourceFingerprint = fingerprint,
                Translations = new Dictionary<int, string>
                {
                    [1] = "Cached translation",
                    [3] = "Cached third translation"
                }
            });
        checkpointService
            .Setup(service => service.DeleteAsync(
                request.Id,
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(
            sourcePath,
            checkpointService.Object,
            new Dictionary<string, string>
            {
                [SettingKeys.Translation.RemoveLanguageTag] = "false",
                [SettingKeys.Translation.StripSubtitleFormatting] = "false",
                [SettingKeys.Translation.SubtitleOutputMode] = "ass"
            });

        var result = await service.CompleteAsync(
            request,
            new Dictionary<int, string> { [1] = "Manual edit" },
            new HashSet<int>(),
            "Manual ASS edit.",
            CancellationToken.None);

        Assert.True(result.Completed);
        var output = await File.ReadAllTextAsync(result.OutputPath!);
        Assert.Contains(@"{\an8}", output);
        Assert.Contains(@"{\t(0,500,\fs30)}", output);
        Assert.Contains(@"{\k20}", output);
        Assert.Contains(@"{\k30}", output);
        Assert.Contains("Manual", output);
        Assert.Contains("edit", output);
        Assert.DoesNotContain("Cached translation", output);
    }

    [Fact]
    public async Task CompleteAsync_WhenCheckpointFingerprintIsStale_DoesNotAcceptCachedTranslations()
    {
        var sourcePath = Path.Combine(_tempDirectory, "stale-fingerprint.en.srt");
        await File.WriteAllTextAsync(sourcePath, CreateSrtSubtitle("Old source line"));
        var staleFingerprint = BuildFallbackFingerprint(sourcePath, "en", "pl", ".srt");
        await File.WriteAllTextAsync(sourcePath, CreateSrtSubtitle("Current source line"));

        var request = new TranslationRequest
        {
            Title = "Stale checkpoint episode",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            SourceSubtitleFormat = ".srt",
            SubtitleOutputMode = "srt",
            RequiredOutputFormats = ".srt",
            SourceSnapshotFingerprint = staleFingerprint,
            MediaType = MediaType.Episode,
            Status = TranslationStatus.Failed,
            WorkloadKind = TranslationWorkloadKind.CustomSource
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var checkpoint = new TranslationCheckpoint
        {
            TranslationRequestId = request.Id,
            SourceFingerprint = staleFingerprint,
            Translations = new Dictionary<int, string>
            {
                [1] = "Stale cached translation"
            }
        };
        TranslationCheckpoint? savedCheckpoint = null;
        var checkpointService = new Mock<ITranslationCheckpointService>();
        checkpointService
            .Setup(service => service.LoadByRequestIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkpoint);
         checkpointService
             .Setup(service => service.SaveCheckpointAsync(
                 It.IsAny<TranslationCheckpoint>(),
                 It.IsAny<CancellationToken>()))
             .Callback<TranslationCheckpoint, CancellationToken>((saved, _) => savedCheckpoint = saved)
             .Returns(Task.CompletedTask);
        checkpointService
            .Setup(service => service.SaveCheckpointAsync(
                It.IsAny<TranslationCheckpoint>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .Callback<TranslationCheckpoint, CancellationToken, string?>((saved, _, _) => savedCheckpoint = saved)
            .Returns(Task.CompletedTask);

        var service = CreateService(
            sourcePath,
            checkpointService.Object,
            new Dictionary<string, string>
            {
                [SettingKeys.Translation.RemoveLanguageTag] = "false",
                [SettingKeys.Translation.StripSubtitleFormatting] = "false",
                [SettingKeys.Translation.SubtitleOutputMode] = "srt"
            });

        var result = await service.CompleteAsync(
            request,
            new Dictionary<int, string>(),
            new HashSet<int>(),
            "Should not accept stale checkpoint.",
            CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Contains("Untranslated ordinary subtitle", result.SkippedReason);
        Assert.NotNull(savedCheckpoint);
        Assert.Empty(savedCheckpoint!.Translations);
        Assert.Equal(
            BuildFallbackFingerprint(sourcePath, "en", "pl", ".srt"),
            savedCheckpoint.SourceFingerprint);
        checkpointService.Verify(
            service => service.DeleteAsync(
                request.Id,
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_WhenRequestFingerprintIsMissing_RejectsCheckpointFromReplacedSource()
    {
        var sourcePath = Path.Combine(_tempDirectory, "replaced-source.en.srt");
        var oldSource = CreateSrtSubtitle("Old source line");
        await File.WriteAllTextAsync(sourcePath, oldSource);
        var oldFingerprint = BuildFallbackFingerprint(sourcePath, "en", "pl", ".srt");

        await File.WriteAllTextAsync(sourcePath, CreateSrtSubtitle("Current source line"));

        var request = new TranslationRequest
        {
            Title = "Replaced source episode",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            SourceSubtitleFormat = ".srt",
            SubtitleOutputMode = "srt",
            RequiredOutputFormats = ".srt",
            Status = TranslationStatus.Failed,
            MediaType = MediaType.Episode,
            WorkloadKind = TranslationWorkloadKind.CustomSource
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var checkpoint = new TranslationCheckpoint
        {
            TranslationRequestId = request.Id,
            SourceFingerprint = oldFingerprint,
            Translations = new Dictionary<int, string>
            {
                [1] = "Stale cached translation"
            }
        };
        TranslationCheckpoint? savedCheckpoint = null;
        var checkpointService = new Mock<ITranslationCheckpointService>();
        checkpointService
            .Setup(service => service.LoadByRequestIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkpoint);
         checkpointService
             .Setup(service => service.SaveCheckpointAsync(
                 It.IsAny<TranslationCheckpoint>(),
                 It.IsAny<CancellationToken>()))
             .Callback<TranslationCheckpoint, CancellationToken>((saved, _) => savedCheckpoint = saved)
             .Returns(Task.CompletedTask);
        checkpointService
            .Setup(service => service.SaveCheckpointAsync(
                It.IsAny<TranslationCheckpoint>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .Callback<TranslationCheckpoint, CancellationToken, string?>((saved, _, _) => savedCheckpoint = saved)
            .Returns(Task.CompletedTask);

        var service = CreateService(
            sourcePath,
            checkpointService.Object,
            new Dictionary<string, string>
            {
                [SettingKeys.Translation.RemoveLanguageTag] = "false",
                [SettingKeys.Translation.StripSubtitleFormatting] = "false",
                [SettingKeys.Translation.SubtitleOutputMode] = "srt"
            });

        var result = await service.CompleteAsync(
            request,
            new Dictionary<int, string>(),
            new HashSet<int>(),
            "Should reject the replaced source checkpoint.",
            CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Contains("Untranslated ordinary subtitle", result.SkippedReason);
        Assert.NotNull(savedCheckpoint);
        Assert.Empty(savedCheckpoint!.Translations);
        Assert.Equal(
            BuildFallbackFingerprint(sourcePath, "en", "pl", ".srt"),
            savedCheckpoint.SourceFingerprint);
    }

    [Fact]
    public async Task CompleteAsync_WhenSourcePreservedMarkerBelongsToReplacedSource_ClearsMarkerBeforeCompletion()
    {
        var sourcePath = Path.Combine(_tempDirectory, "stale-source-marker.en.srt");
        await File.WriteAllTextAsync(sourcePath, CreateSrtSubtitle("Old dialogue"));
        var staleFingerprint = BuildFallbackFingerprint(sourcePath, "en", "pl", ".srt");
        await File.WriteAllTextAsync(sourcePath, CreateSrtSubtitle("Current dialogue"));

        var request = new TranslationRequest
        {
            Title = "Stale source marker episode",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            SourceSubtitleFormat = ".srt",
            SubtitleOutputMode = "srt",
            RequiredOutputFormats = ".srt",
            SourceSnapshotFingerprint = staleFingerprint,
            MediaType = MediaType.Episode,
            Status = TranslationStatus.Failed,
            WorkloadKind = TranslationWorkloadKind.CustomSource
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var checkpoint = new TranslationCheckpoint
        {
            TranslationRequestId = request.Id,
            SourceFingerprint = staleFingerprint,
            Translations = new Dictionary<int, string>
            {
                [1] = "Old dialogue"
            },
            SourcePreservedPositions = [1]
        };
        TranslationCheckpoint? savedCheckpoint = null;
        var checkpointService = new Mock<ITranslationCheckpointService>();
        checkpointService
            .Setup(service => service.LoadByRequestIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkpoint);
         checkpointService
             .Setup(service => service.SaveCheckpointAsync(
                 It.IsAny<TranslationCheckpoint>(),
                 It.IsAny<CancellationToken>()))
             .Callback<TranslationCheckpoint, CancellationToken>((saved, _) => savedCheckpoint = saved)
             .Returns(Task.CompletedTask);
        checkpointService
            .Setup(service => service.SaveCheckpointAsync(
                It.IsAny<TranslationCheckpoint>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .Callback<TranslationCheckpoint, CancellationToken, string?>((saved, _, _) => savedCheckpoint = saved)
            .Returns(Task.CompletedTask);

        var service = CreateService(
            sourcePath,
            checkpointService.Object,
            new Dictionary<string, string>
            {
                [SettingKeys.Translation.RemoveLanguageTag] = "false",
                [SettingKeys.Translation.StripSubtitleFormatting] = "false",
                [SettingKeys.Translation.SubtitleOutputMode] = "srt"
            });

        var result = await service.CompleteAsync(
            request,
            new Dictionary<int, string>(),
            new HashSet<int> { 1 },
            "Stale source marker must not authorize completion.",
            CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Contains("Untranslated ordinary subtitle", result.SkippedReason);
        Assert.NotNull(savedCheckpoint);
        Assert.Empty(savedCheckpoint!.Translations);
        Assert.Empty(savedCheckpoint.SourcePreservedPositions);
        Assert.Equal(
            BuildFallbackFingerprint(sourcePath, "en", "pl", ".srt"),
            savedCheckpoint.SourceFingerprint);
    }

    [Fact]
    public async Task CompleteAsync_WhenEmbeddingIsConfigured_EmbedsAgainstManagedMediaPathWithoutCacheSidecar()
    {
        var mediaDirectory = Path.Combine(_tempDirectory, "media");
        var cacheDirectory = Path.Combine(_tempDirectory, "cache");
        Directory.CreateDirectory(mediaDirectory);
        Directory.CreateDirectory(cacheDirectory);

        var mediaPath = Path.Combine(mediaDirectory, "managed-movie.mkv");
        var sourcePath = Path.Combine(cacheDirectory, "movie-1-stream-0-eng.srt");
        await File.WriteAllTextAsync(mediaPath, "fake mkv");
        await File.WriteAllTextAsync(sourcePath, CreateSrtSubtitle("Hello"));
        var fingerprint = BuildFallbackFingerprint(sourcePath, "en", "pl", ".srt");

        var movie = new Movie
        {
            RadarrId = 1,
            Title = "Managed movie",
            FileName = Path.GetFileName(mediaPath),
            Path = mediaDirectory,
            DateAdded = DateTime.UtcNow
        };
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var request = new TranslationRequest
        {
            MediaId = movie.Id,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            SourceSubtitleFormat = ".srt",
            SubtitleOutputMode = "srt",
            RequiredOutputFormats = ".srt",
            SourceSnapshotFingerprint = fingerprint,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Failed,
            WorkloadKind = TranslationWorkloadKind.Library
        };
        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var checkpointService = new Mock<ITranslationCheckpointService>();
        checkpointService
            .Setup(service => service.LoadByRequestIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranslationCheckpoint
            {
                TranslationRequestId = request.Id,
                SourceFingerprint = fingerprint,
                Translations = new Dictionary<int, string> { [1] = "Czesc" }
            });
        checkpointService
            .Setup(service => service.DeleteAsync(
                request.Id,
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var cacheService = new Mock<IEmbeddedSubtitleCacheService>();
        cacheService
            .Setup(service => service.IsManagedCachePath(sourcePath))
            .Returns(true);

        var mkvEmbeddingService = new Mock<IMkvEmbeddingService>();
        mkvEmbeddingService
            .Setup(service => service.WouldExceedPathLimit(It.IsAny<string>()))
            .Returns(false);
        string? stagedSubtitlePath = null;
        mkvEmbeddingService
            .Setup(service => service.EmbedSubtitlesAsync(
                mediaPath,
                It.IsAny<IReadOnlyCollection<MkvSubtitleInput>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyCollection<MkvSubtitleInput>, CancellationToken>(
                (_, inputs, _) => stagedSubtitlePath = inputs.Single().SubtitlePath)
            .ReturnsAsync(new MkvEmbedResult(true, mediaPath));

        var service = CreateService(
            sourcePath,
            checkpointService.Object,
            new Dictionary<string, string>
            {
                [SettingKeys.Translation.RemoveLanguageTag] = "false",
                [SettingKeys.Translation.StripSubtitleFormatting] = "false",
                [SettingKeys.Translation.SubtitleOutputMode] = "srt",
                [SettingKeys.Translation.EmbedInContainer] = "true"
            },
            mkvEmbeddingService: mkvEmbeddingService.Object,
            embeddedSubtitleCacheService: cacheService.Object);

        var result = await service.CompleteAsync(
            request,
            new Dictionary<int, string>(),
            new HashSet<int>(),
            "Embedded output.",
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.StartsWith("mkv-embedded:stream0|", result.OutputPath);
        Assert.Equal(TranslationStatus.Completed, (await _dbContext.TranslationRequests.FindAsync(request.Id))!.Status);
        Assert.NotNull(stagedSubtitlePath);
        Assert.False(File.Exists(stagedSubtitlePath));

        var sidecarPath = new SubtitleService(NullLogger<SubtitleService>.Instance)
            .CreateFallbackPaths(mediaPath, "pl", string.Empty, string.Empty, ".srt")
            .First();
        Assert.False(File.Exists(sidecarPath));
        mkvEmbeddingService.Verify(
            service => service.EmbedSubtitlesAsync(
                mediaPath,
                It.Is<IReadOnlyCollection<MkvSubtitleInput>>(inputs =>
                    inputs.Count == 1 &&
                    inputs.Single().LanguageCode == "pl" &&
                    inputs.Single().TrackName == "pl (Lingarr)"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        mkvEmbeddingService.Verify(
            service => service.EmbedSubtitleAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_WhenBatchEmbeddingFailsForAssAndSrt_LeavesContainerUntouched()
    {
        var mediaDirectory = Path.Combine(_tempDirectory, "batch-failure-media");
        var cacheDirectory = Path.Combine(_tempDirectory, "batch-failure-cache");
        Directory.CreateDirectory(mediaDirectory);
        Directory.CreateDirectory(cacheDirectory);

        var mediaPath = Path.Combine(mediaDirectory, "batch-failure.mkv");
        var sourcePath = Path.Combine(cacheDirectory, "batch-failure-stream-0-eng.ass");
        const string originalMediaContent = "original container bytes";
        await File.WriteAllTextAsync(mediaPath, originalMediaContent);
        await File.WriteAllTextAsync(sourcePath, CreateAssSubtitle());
        var fingerprint = BuildFallbackFingerprint(sourcePath, "en", "pl", ".ass");

        var movie = new Movie
        {
            RadarrId = 3,
            Title = "Batch failure movie",
            FileName = Path.GetFileName(mediaPath),
            Path = mediaDirectory,
            DateAdded = DateTime.UtcNow
        };
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var request = new TranslationRequest
        {
            MediaId = movie.Id,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            SourceSubtitleFormat = ".ass",
            SubtitleOutputMode = "both",
            RequiredOutputFormats = ".ass,.srt",
            SourceSnapshotFingerprint = fingerprint,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Failed,
            WorkloadKind = TranslationWorkloadKind.Library
        };
        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var checkpointService = new Mock<ITranslationCheckpointService>();
        checkpointService
            .Setup(service => service.LoadByRequestIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranslationCheckpoint
            {
                TranslationRequestId = request.Id,
                SourceFingerprint = fingerprint,
                Translations = new Dictionary<int, string>
                {
                    [1] = "Przetlumaczony tekst",
                    [3] = "Echowane zdanie"
                }
            });

        var cacheService = new Mock<IEmbeddedSubtitleCacheService>();
        cacheService
            .Setup(service => service.IsManagedCachePath(sourcePath))
            .Returns(true);

        var mkvEmbeddingService = new Mock<IMkvEmbeddingService>();
        mkvEmbeddingService
            .Setup(service => service.WouldExceedPathLimit(It.IsAny<string>()))
            .Returns(false);
        List<MkvSubtitleInput>? capturedInputs = null;
        mkvEmbeddingService
            .Setup(service => service.EmbedSubtitlesAsync(
                mediaPath,
                It.IsAny<IReadOnlyCollection<MkvSubtitleInput>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyCollection<MkvSubtitleInput>, CancellationToken>(
                (_, inputs, _) => capturedInputs = inputs.ToList())
            .ReturnsAsync(new MkvEmbedResult(false, Error: "batch merge failed"));

        var service = CreateService(
            sourcePath,
            checkpointService.Object,
            new Dictionary<string, string>
            {
                [SettingKeys.Translation.RemoveLanguageTag] = "false",
                [SettingKeys.Translation.StripSubtitleFormatting] = "false",
                [SettingKeys.Translation.SubtitleOutputMode] = "both",
                [SettingKeys.Translation.EmbedInContainer] = "true"
            },
            mkvEmbeddingService: mkvEmbeddingService.Object,
            embeddedSubtitleCacheService: cacheService.Object);

        var result = await service.CompleteAsync(
            request,
            new Dictionary<int, string>(),
            new HashSet<int>(),
            "Batch embedding must remain retryable.",
            CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Contains("batch merge failed", result.SkippedReason);
        Assert.Equal(originalMediaContent, await File.ReadAllTextAsync(mediaPath));
        Assert.Equal(
            TranslationStatus.Failed,
            (await _dbContext.TranslationRequests.FindAsync(request.Id))!.Status);
        Assert.NotNull(capturedInputs);
        Assert.Equal(2, capturedInputs!.Count);
        Assert.Contains(capturedInputs, input => input.SubtitlePath.EndsWith(".ass", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(capturedInputs, input => input.SubtitlePath.EndsWith(".srt", StringComparison.OrdinalIgnoreCase));
        mkvEmbeddingService.Verify(
            service => service.EmbedSubtitlesAsync(
                mediaPath,
                It.IsAny<IReadOnlyCollection<MkvSubtitleInput>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        mkvEmbeddingService.Verify(
            service => service.EmbedSubtitleAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_WhenBatchPublicationLosesCompletionRace_RestoresOriginalContainer()
    {
        var mediaDirectory = Path.Combine(_tempDirectory, "batch-race-media");
        var cacheDirectory = Path.Combine(_tempDirectory, "batch-race-cache");
        Directory.CreateDirectory(mediaDirectory);
        Directory.CreateDirectory(cacheDirectory);

        var mediaPath = Path.Combine(mediaDirectory, "batch-race.mkv");
        var sourcePath = Path.Combine(cacheDirectory, "batch-race-stream-0-eng.ass");
        const string originalMediaContent = "original race container bytes";
        await File.WriteAllTextAsync(mediaPath, originalMediaContent);
        await File.WriteAllTextAsync(sourcePath, CreateAssSubtitle());
        var fingerprint = BuildFallbackFingerprint(sourcePath, "en", "pl", ".ass");

        var movie = new Movie
        {
            RadarrId = 4,
            Title = "Batch race movie",
            FileName = Path.GetFileName(mediaPath),
            Path = mediaDirectory,
            DateAdded = DateTime.UtcNow
        };
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var request = new TranslationRequest
        {
            MediaId = movie.Id,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            SourceSubtitleFormat = ".ass",
            SubtitleOutputMode = "both",
            RequiredOutputFormats = ".ass,.srt",
            SourceSnapshotFingerprint = fingerprint,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Failed,
            WorkloadKind = TranslationWorkloadKind.Library
        };
        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var checkpointService = new Mock<ITranslationCheckpointService>();
        checkpointService
            .Setup(service => service.LoadByRequestIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranslationCheckpoint
            {
                TranslationRequestId = request.Id,
                SourceFingerprint = fingerprint,
                Translations = new Dictionary<int, string>
                {
                    [1] = "Przetłumaczony tekst",
                    [3] = "Echowane zdanie"
                }
            });

        var cacheService = new Mock<IEmbeddedSubtitleCacheService>();
        cacheService
            .Setup(service => service.IsManagedCachePath(sourcePath))
            .Returns(true);

        var externalContextOptions = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(_connection)
            .Options;
        var mkvEmbeddingService = new Mock<IMkvEmbeddingService>();
        mkvEmbeddingService
            .Setup(service => service.WouldExceedPathLimit(It.IsAny<string>()))
            .Returns(false);
        mkvEmbeddingService
            .Setup(service => service.EmbedSubtitlesAsync(
                mediaPath,
                It.IsAny<IReadOnlyCollection<MkvSubtitleInput>>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, IReadOnlyCollection<MkvSubtitleInput>, CancellationToken>(
                async (path, _, _) =>
                {
                    await File.WriteAllTextAsync(path, "batch publication winner");
                    await using var externalContext = new LingarrDbContext(externalContextOptions);
                    await externalContext.TranslationRequests
                        .Where(item => item.Id == request.Id)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(item => item.Status, TranslationStatus.Pending)
                            .SetProperty(item => item.IsActive, (bool?)true)
                            .SetProperty(item => item.JobId, (string?)null));
                    return new MkvEmbedResult(true, path);
                });

        var service = CreateService(
            sourcePath,
            checkpointService.Object,
            new Dictionary<string, string>
            {
                [SettingKeys.Translation.RemoveLanguageTag] = "false",
                [SettingKeys.Translation.StripSubtitleFormatting] = "false",
                [SettingKeys.Translation.SubtitleOutputMode] = "both",
                [SettingKeys.Translation.EmbedInContainer] = "true"
            },
            mkvEmbeddingService: mkvEmbeddingService.Object,
            embeddedSubtitleCacheService: cacheService.Object);

        var result = await service.CompleteAsync(
            request,
            new Dictionary<int, string>(),
            new HashSet<int>(),
            "Completion race must not leave a partial container publication.",
            CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Contains(TranslationStatus.Pending.ToString(), result.SkippedReason);
        Assert.Equal(originalMediaContent, await File.ReadAllTextAsync(mediaPath));
        Assert.Equal(
            TranslationStatus.Pending,
            (await _dbContext.TranslationRequests.FindAsync(request.Id))!.Status);
        Assert.Empty(Directory.EnumerateFiles(
            mediaDirectory,
            "*failed-compare*.bak*",
            SearchOption.AllDirectories));
        mkvEmbeddingService.Verify(
            service => service.EmbedSubtitlesAsync(
                mediaPath,
                It.Is<IReadOnlyCollection<MkvSubtitleInput>>(inputs => inputs.Count == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_WhenReplacementClaimsDuringEmbedding_WaitsForLeaseAndWinsWithoutStaleRollback()
    {
        var mediaDirectory = Path.Combine(_tempDirectory, "replacement-lease-media");
        var cacheDirectory = Path.Combine(_tempDirectory, "replacement-lease-cache");
        Directory.CreateDirectory(mediaDirectory);
        Directory.CreateDirectory(cacheDirectory);

        var mediaPath = Path.Combine(mediaDirectory, "replacement-lease.mkv");
        var sourcePath = Path.Combine(cacheDirectory, "replacement-lease-stream-0-eng.srt");
        await File.WriteAllTextAsync(mediaPath, "original container");
        await File.WriteAllTextAsync(sourcePath, CreateSrtSubtitle("Hello"));
        var fingerprint = BuildFallbackFingerprint(sourcePath, "en", "pl", ".srt");

        var movie = new Movie
        {
            RadarrId = 41,
            Title = "Replacement lease movie",
            FileName = Path.GetFileName(mediaPath),
            Path = mediaDirectory,
            DateAdded = DateTime.UtcNow
        };
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var request = new TranslationRequest
        {
            MediaId = movie.Id,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            SourceSubtitleFormat = ".srt",
            SubtitleOutputMode = "srt",
            RequiredOutputFormats = ".srt",
            SourceSnapshotFingerprint = fingerprint,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Failed,
            WorkloadKind = TranslationWorkloadKind.Library
        };
        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var checkpointService = new Mock<ITranslationCheckpointService>();
        checkpointService
            .Setup(service => service.LoadByRequestIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new TranslationCheckpoint
            {
                TranslationRequestId = request.Id,
                SourceFingerprint = fingerprint,
                Translations = new Dictionary<int, string> { [1] = "Czesc" }
            });

        var cacheService = new Mock<IEmbeddedSubtitleCacheService>();
        cacheService
            .Setup(service => service.IsManagedCachePath(sourcePath))
            .Returns(true);

        var oldEmbeddingStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementEmbeddingStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOldEmbedding = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var oldEmbeddingService = new Mock<IMkvEmbeddingService>();
        oldEmbeddingService
            .Setup(service => service.WouldExceedPathLimit(It.IsAny<string>()))
            .Returns(false);
        oldEmbeddingService
            .Setup(service => service.EmbedSubtitlesAsync(
                mediaPath,
                It.IsAny<IReadOnlyCollection<MkvSubtitleInput>>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, IReadOnlyCollection<MkvSubtitleInput>, CancellationToken>(
                async (path, _, _) =>
                {
                    await File.WriteAllTextAsync(path, "stale container");
                    oldEmbeddingStarted.SetResult(true);
                    await releaseOldEmbedding.Task;
                    return new MkvEmbedResult(true, path);
                });

        var replacementEmbeddingService = new Mock<IMkvEmbeddingService>();
        replacementEmbeddingService
            .Setup(service => service.WouldExceedPathLimit(It.IsAny<string>()))
            .Returns(false);
        replacementEmbeddingService
            .Setup(service => service.EmbedSubtitlesAsync(
                mediaPath,
                It.IsAny<IReadOnlyCollection<MkvSubtitleInput>>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, IReadOnlyCollection<MkvSubtitleInput>, CancellationToken>(
                async (path, _, _) =>
                {
                    replacementEmbeddingStarted.SetResult(true);
                    await File.WriteAllTextAsync(path, "replacement container");
                    return new MkvEmbedResult(true, path);
                });

        var externalContextOptions = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(_connection)
            .Options;
        await using var replacementContext = new LingarrDbContext(externalContextOptions);

        var settings = new Dictionary<string, string>
        {
            [SettingKeys.Translation.RemoveLanguageTag] = "false",
            [SettingKeys.Translation.StripSubtitleFormatting] = "false",
            [SettingKeys.Translation.SubtitleOutputMode] = "srt",
            [SettingKeys.Translation.EmbedInContainer] = "true"
        };
        var oldService = CreateService(
            sourcePath,
            checkpointService.Object,
            settings,
            mkvEmbeddingService: oldEmbeddingService.Object,
            embeddedSubtitleCacheService: cacheService.Object);
        var replacementService = CreateService(
            sourcePath,
            checkpointService.Object,
            settings,
            mkvEmbeddingService: replacementEmbeddingService.Object,
            embeddedSubtitleCacheService: cacheService.Object,
            dbContext: replacementContext);

        var oldTask = oldService.CompleteAsync(
            request,
            new Dictionary<int, string>(),
            new HashSet<int>(),
            "Old publication should lose to the replacement.",
            CancellationToken.None);

        await oldEmbeddingStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await replacementContext.TranslationRequests
            .Where(item => item.Id == request.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, TranslationStatus.Failed)
                .SetProperty(item => item.IsActive, (bool?)null)
                .SetProperty(item => item.JobId, (string?)null));

        var replacementTask = replacementService.CompleteAsync(
            request,
            new Dictionary<int, string>(),
            new HashSet<int>(),
            "Replacement publication should win.",
            CancellationToken.None);
        var completedBeforeRelease = await Task.WhenAny(
            replacementEmbeddingStarted.Task,
            Task.Delay(TimeSpan.FromMilliseconds(150)));
        Assert.NotSame(replacementEmbeddingStarted.Task, completedBeforeRelease);

        releaseOldEmbedding.SetResult(true);
        var oldResult = await oldTask;
        var replacementResult = await replacementTask;

        Assert.False(oldResult.Completed);
        Assert.True(replacementResult.Completed);
        Assert.Equal("replacement container", await File.ReadAllTextAsync(mediaPath));
        var persistedRequest = await replacementContext.TranslationRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == request.Id);
        Assert.Equal(TranslationStatus.Completed, persistedRequest.Status);
    }

    [Fact]
    public async Task CompleteAsync_WhenRequiredEmbeddingFails_LeavesRequestFailedAndLogsRetryableReason()
    {
        var mediaDirectory = Path.Combine(_tempDirectory, "failed-embed-media");
        var cacheDirectory = Path.Combine(_tempDirectory, "failed-embed-cache");
        Directory.CreateDirectory(mediaDirectory);
        Directory.CreateDirectory(cacheDirectory);

        var mediaPath = Path.Combine(mediaDirectory, "managed-movie.mkv");
        var sourcePath = Path.Combine(cacheDirectory, "movie-2-stream-0-eng.srt");
        await File.WriteAllTextAsync(mediaPath, "fake mkv");
        await File.WriteAllTextAsync(sourcePath, CreateSrtSubtitle("Hello"));
        var fingerprint = BuildFallbackFingerprint(sourcePath, "en", "pl", ".srt");

        var movie = new Movie
        {
            RadarrId = 2,
            Title = "Failed managed movie",
            FileName = Path.GetFileName(mediaPath),
            Path = mediaDirectory,
            DateAdded = DateTime.UtcNow
        };
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var request = new TranslationRequest
        {
            MediaId = movie.Id,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            SourceSubtitleFormat = ".srt",
            SubtitleOutputMode = "srt",
            RequiredOutputFormats = ".srt",
            SourceSnapshotFingerprint = fingerprint,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Failed,
            WorkloadKind = TranslationWorkloadKind.Library
        };
        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var checkpointService = new Mock<ITranslationCheckpointService>();
        checkpointService
            .Setup(service => service.LoadByRequestIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranslationCheckpoint
            {
                TranslationRequestId = request.Id,
                SourceFingerprint = fingerprint,
                Translations = new Dictionary<int, string> { [1] = "Czesc" }
            });

        var cacheService = new Mock<IEmbeddedSubtitleCacheService>();
        cacheService
            .Setup(service => service.IsManagedCachePath(sourcePath))
            .Returns(true);

        var mkvEmbeddingService = new Mock<IMkvEmbeddingService>();
        mkvEmbeddingService
            .Setup(service => service.WouldExceedPathLimit(It.IsAny<string>()))
            .Returns(false);
        mkvEmbeddingService
            .Setup(service => service.EmbedSubtitlesAsync(
                mediaPath,
                It.IsAny<IReadOnlyCollection<MkvSubtitleInput>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MkvEmbedResult(false, Error: "mkvmerge unavailable"));

        var service = CreateService(
            sourcePath,
            checkpointService.Object,
            new Dictionary<string, string>
            {
                [SettingKeys.Translation.RemoveLanguageTag] = "false",
                [SettingKeys.Translation.StripSubtitleFormatting] = "false",
                [SettingKeys.Translation.SubtitleOutputMode] = "srt",
                [SettingKeys.Translation.EmbedInContainer] = "true"
            },
            mkvEmbeddingService: mkvEmbeddingService.Object,
            embeddedSubtitleCacheService: cacheService.Object);

        var result = await service.CompleteAsync(
            request,
            new Dictionary<int, string>(),
            new HashSet<int>(),
            "Embedding should remain retryable.",
            CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Contains("mkvmerge unavailable", result.SkippedReason);

        var persistedRequest = await _dbContext.TranslationRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == request.Id);
        Assert.Equal(TranslationStatus.Failed, persistedRequest.Status);
        Assert.Null(persistedRequest.TranslatedSubtitle);

        var failureLog = await _dbContext.TranslationRequestLogs
            .AsNoTracking()
            .Where(log => log.TranslationRequestId == request.Id)
            .OrderByDescending(log => log.Id)
            .FirstAsync();
        Assert.Contains("mkvmerge unavailable", failureLog.Details);
    }

    [Fact]
    public async Task CompleteAsync_WhenPublicationFailsAfterEarlierOutputMove_RollsBackAndLeavesRequestFailed()
    {
        var sourcePath = Path.Combine(_tempDirectory, "publication-failure.en.ass");
        await File.WriteAllTextAsync(sourcePath, CreateAssSubtitle());

        var subtitleService = new SubtitleService(NullLogger<SubtitleService>.Instance);
        var assPath = subtitleService
            .CreateFallbackPaths(sourcePath, "pl", string.Empty, string.Empty, ".ass")
            .First();
        var srtPath = subtitleService
            .CreateFallbackPaths(sourcePath, "pl", string.Empty, string.Empty, ".srt")
            .First();
        Directory.CreateDirectory(srtPath);

        var request = new TranslationRequest
        {
            Title = "Publication failure episode",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            SourceSubtitleFormat = ".ass",
            SubtitleOutputMode = "both",
            RequiredOutputFormats = ".ass,.srt",
            SourceSnapshotFingerprint = BuildFallbackFingerprint(sourcePath, "en", "pl", ".ass"),
            MediaType = MediaType.Episode,
            Status = TranslationStatus.Failed,
            WorkloadKind = TranslationWorkloadKind.CustomSource
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var checkpointService = new Mock<ITranslationCheckpointService>();
        checkpointService
            .Setup(service => service.LoadByRequestIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranslationCheckpoint
            {
                TranslationRequestId = request.Id,
                SourceFingerprint = BuildFallbackFingerprint(sourcePath, "en", "pl", ".ass"),
                Translations = new Dictionary<int, string>
                {
                    [1] = "Przetlumaczony tekst",
                    [3] = "Echowane zdanie"
                }
            });

        var service = CreateService(
            sourcePath,
            checkpointService.Object,
            new Dictionary<string, string>
            {
                [SettingKeys.Translation.RemoveLanguageTag] = "false",
                [SettingKeys.Translation.StripSubtitleFormatting] = "false",
                [SettingKeys.Translation.SubtitleOutputMode] = "both"
            });

        await Assert.ThrowsAnyAsync<Exception>(() => service.CompleteAsync(
            request,
            new Dictionary<int, string>(),
            new HashSet<int> { 3 },
            "Publication should be retryable.",
            CancellationToken.None));

        var persistedRequest = await _dbContext.TranslationRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == request.Id);
        Assert.Equal(TranslationStatus.Failed, persistedRequest.Status);
        Assert.Null(persistedRequest.JobId);
        Assert.Null(persistedRequest.IsActive);
        Assert.Null(persistedRequest.TranslatedSubtitle);
        Assert.False(File.Exists(assPath));
        Assert.True(Directory.Exists(srtPath));
        Assert.Empty(Directory.EnumerateFiles(
            _tempDirectory,
            "*failed-compare*.tmp*",
            SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(
            _tempDirectory,
            "*failed-compare*.bak*",
            SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(TranslationStatus.Pending)]
    [InlineData(TranslationStatus.Cancelled)]
    public async Task CompleteAsync_WhenRetryOrCancelWinsRace_DoesNotCompleteOrDeleteWinnerOutput(
        TranslationStatus winningStatus)
    {
        var sourcePath = Path.Combine(_tempDirectory, "race.en.ass");
        await File.WriteAllTextAsync(sourcePath, CreateAssSubtitle());

        var subtitleService = new SubtitleService(NullLogger<SubtitleService>.Instance);
        var winnerOutputPath = subtitleService
            .CreateFallbackPaths(sourcePath, "pl", string.Empty, string.Empty, ".ass")
            .First();
        await File.WriteAllTextAsync(winnerOutputPath, "winner output");

        var request = new TranslationRequest
        {
            Title = "Race episode",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            SourceSubtitleFormat = ".ass",
            SubtitleOutputMode = "ass",
            RequiredOutputFormats = ".ass",
            SourceSnapshotFingerprint = BuildFallbackFingerprint(sourcePath, "en", "pl", ".ass"),
            MediaType = MediaType.Episode,
            Status = TranslationStatus.Failed,
            WorkloadKind = TranslationWorkloadKind.CustomSource
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var checkpoint = new TranslationCheckpoint
        {
            TranslationRequestId = request.Id,
            SourceFingerprint = BuildFallbackFingerprint(sourcePath, "en", "pl", ".ass"),
            Translations =
            {
                [1] = "Przetłumaczony tekst",
                [3] = "Echowane zdanie"
            }
        };
        var checkpointService = new Mock<ITranslationCheckpointService>();
        checkpointService
            .Setup(service => service.LoadByRequestIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkpoint);

        var reachedFinalCommit = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFinalCommit = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateService(
            sourcePath,
            checkpointService.Object,
            new Dictionary<string, string>
            {
                [SettingKeys.Translation.RemoveLanguageTag] = "false",
                [SettingKeys.Translation.StripSubtitleFormatting] = "false"
            },
            async () =>
            {
                reachedFinalCommit.SetResult(true);
                await releaseFinalCommit.Task;
            });

        var completionTask = service.CompleteAsync(
            request,
            new Dictionary<int, string>(),
            new HashSet<int>(),
            "Should not complete after the winner takes the request.",
            CancellationToken.None);

        await reachedFinalCommit.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            var externalContextOptions = new DbContextOptionsBuilder<LingarrDbContext>()
                .UseSqlite(_connection)
                .Options;
            await using var externalContext = new LingarrDbContext(externalContextOptions);
            var isActive = winningStatus == TranslationStatus.Pending ? true : (bool?)null;
            var serializedWinnerOutputPaths = JsonSerializer.Serialize(new[] { winnerOutputPath });
            await externalContext.TranslationRequests
                .Where(item => item.Id == request.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, winningStatus)
                    .SetProperty(item => item.IsActive, isActive)
                    .SetProperty(item => item.JobId, (string?)null)
                    .SetProperty(item => item.TranslatedSubtitle, winnerOutputPath)
                    .SetProperty(item => item.GeneratedSubtitlePaths, serializedWinnerOutputPaths));
        }
        finally
        {
            releaseFinalCommit.SetResult(true);
        }

        var result = await completionTask;

        Assert.False(result.Completed);
        Assert.False(result.AlreadyCompleted);
        Assert.Contains(winningStatus.ToString(), result.SkippedReason);

        var persistedRequest = await _dbContext.TranslationRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == request.Id);
        Assert.Equal(winningStatus, persistedRequest.Status);
        Assert.Equal(winnerOutputPath, persistedRequest.TranslatedSubtitle);
        Assert.Equal(
            JsonSerializer.Serialize(new[] { winnerOutputPath }),
            persistedRequest.GeneratedSubtitlePaths);
        Assert.Equal("winner output", await File.ReadAllTextAsync(winnerOutputPath));
        Assert.Empty(Directory.EnumerateFiles(
            _tempDirectory,
            "*failed-compare*.tmp*",
            SearchOption.AllDirectories));
        checkpointService.Verify(
            service => service.DeleteAsync(
                request.Id,
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task PublishCompletedEditsAsync_WhenCompletedRequestHasFreshForeignClaim_IsBlocked()
    {
        var request = new TranslationRequest
        {
            Title = "Episode",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SourceSubtitleFormat = ".ass",
            SubtitleOutputMode = "srt",
            MediaType = MediaType.Episode,
            Status = TranslationStatus.Completed,
            WorkloadKind = TranslationWorkloadKind.CustomSource,
            JobId = "active-claim",
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var checkpointService = new Mock<ITranslationCheckpointService>();
        var service = CreateService(
            Path.Combine(_tempDirectory, "missing.ass"),
            checkpointService.Object,
            new Dictionary<string, string>(),
            beforeFinalCommitAsync: () => throw new InvalidOperationException("Boom after claim"));

        var result = await service.PublishCompletedEditsAsync(
            request,
            Path.Combine(_tempDirectory, "missing.ass"),
            new List<SubtitleItem>(),
            CancellationToken.None);

        // A fresh foreign claim must block the publish before any work happens.
        Assert.False(result.Completed);
        Assert.Contains("changed while saving edits", result.SkippedReason);
    }

    [Fact]
    public async Task PublishCompletedEditsAsync_WhenCompletedRequestHasStaleClaim_ReclaimsAndProceeds()
    {
        var request = new TranslationRequest
        {
            Title = "Episode",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SourceSubtitleFormat = ".ass",
            SubtitleOutputMode = "srt",
            MediaType = MediaType.Episode,
            Status = TranslationStatus.Completed,
            WorkloadKind = TranslationWorkloadKind.CustomSource,
            JobId = "crashed-claim",
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        // The DbContext forces UpdatedAt to UtcNow on save; age the claim directly
        // in the database to simulate a claim left behind by a crashed attempt.
        await _dbContext.TranslationRequests
            .Where(item => item.Id == request.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.UpdatedAt, DateTime.UtcNow.AddMinutes(-31)));

        var checkpointService = new Mock<ITranslationCheckpointService>();
        var service = CreateService(
            Path.Combine(_tempDirectory, "missing.ass"),
            checkpointService.Object,
            new Dictionary<string, string>(),
            beforeFinalCommitAsync: () => throw new InvalidOperationException("Boom after claim"));

        var result = await service.PublishCompletedEditsAsync(
            request,
            Path.Combine(_tempDirectory, "missing.ass"),
            new List<SubtitleItem>(),
            CancellationToken.None);

        // The stale claim must be reclaimed: the flow proceeds past the claim and
        // fails at the injected hook with its own message instead of the claim error.
        Assert.False(result.Completed);
        Assert.DoesNotContain("changed while saving edits", result.SkippedReason ?? string.Empty);
        Assert.Equal("Boom after claim", result.SkippedReason);
    }

    [Fact]
    public async Task PublishCompletedEditsAsync_WhenEmbeddingFailsWithoutTakeover_RollsBackAndKeepsCommittedOutput()
    {
        var mediaDirectory = Path.Combine(_tempDirectory, "pce-no-takeover-media");
        var cacheDirectory = Path.Combine(_tempDirectory, "pce-no-takeover-cache");
        Directory.CreateDirectory(mediaDirectory);
        Directory.CreateDirectory(cacheDirectory);

        var mediaPath = Path.Combine(mediaDirectory, "managed-movie.mkv");
        var sourcePath = Path.Combine(cacheDirectory, "movie-3-stream-0-eng.srt");
        await File.WriteAllTextAsync(mediaPath, "clean committed container");
        await File.WriteAllTextAsync(sourcePath, CreateSrtSubtitle("Hello"));
        var fingerprint = BuildFallbackFingerprint(sourcePath, "en", "pl", ".srt");

        var movie = new Movie
        {
            RadarrId = 3,
            Title = "PCE movie",
            FileName = Path.GetFileName(mediaPath),
            Path = mediaDirectory,
            DateAdded = DateTime.UtcNow
        };
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var request = new TranslationRequest
        {
            MediaId = movie.Id,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            SourceSubtitleFormat = ".srt",
            SubtitleOutputMode = "srt",
            RequiredOutputFormats = ".srt",
            SourceSnapshotFingerprint = fingerprint,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Completed,
            WorkloadKind = TranslationWorkloadKind.Library,
            GeneratedSubtitlePaths = System.Text.Json.JsonSerializer.Serialize(
                new[] { $"mkv-embedded:{mediaPath}" })
        };
        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var checkpointService = new Mock<ITranslationCheckpointService>();
        var cacheService = new Mock<IEmbeddedSubtitleCacheService>();
        cacheService
            .Setup(service => service.IsManagedCachePath(sourcePath))
            .Returns(true);

        var mkvEmbeddingService = new Mock<IMkvEmbeddingService>();
        mkvEmbeddingService
            .Setup(service => service.WouldExceedPathLimit(It.IsAny<string>()))
            .Returns(false);
        mkvEmbeddingService
            .Setup(service => service.EmbedSubtitlesAsync(
                mediaPath,
                It.IsAny<IReadOnlyCollection<MkvSubtitleInput>>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, IReadOnlyCollection<MkvSubtitleInput>, CancellationToken>(async (path, _, _) =>
            {
                // The embed partially succeeds (the container is swapped) before failing.
                await File.WriteAllTextAsync(path, "partially embedded container");
                return new MkvEmbedResult(false, Error: "mkvmerge unavailable");
            });

        var service = CreateService(
            sourcePath,
            checkpointService.Object,
            new Dictionary<string, string>
            {
                [SettingKeys.Translation.RemoveLanguageTag] = "false",
                [SettingKeys.Translation.StripSubtitleFormatting] = "false",
                [SettingKeys.Translation.SubtitleOutputMode] = "srt",
                [SettingKeys.Translation.EmbedInContainer] = "true"
            },
            mkvEmbeddingService: mkvEmbeddingService.Object,
            embeddedSubtitleCacheService: cacheService.Object);

        var result = await service.PublishCompletedEditsAsync(
            request,
            sourcePath,
            new List<SubtitleItem>
            {
                new()
                {
                    Position = 1,
                    StartTime = 0,
                    EndTime = 1000,
                    Lines = new List<string> { "Hello" },
                    PlaintextLines = new List<string> { "Hello" },
                    TranslatedLines = new List<string> { "Czesc" }
                }
            },
            CancellationToken.None);

        // No takeover happened (JobId is still this attempt's token): the failed
        // attempt must roll back, restoring the clean committed container from its
        // backup instead of deleting that backup.
        Assert.False(result.Completed);
        Assert.Contains("mkvmerge unavailable", result.SkippedReason);
        Assert.Equal("clean committed container", await File.ReadAllTextAsync(mediaPath));
    }

    [Fact]
    public async Task PublishCompletedEditsAsync_WhenEmbeddingFailsAfterTakeover_DoesNotRollBackTakeoverContainer()
    {
        var mediaDirectory = Path.Combine(_tempDirectory, "pce-takeover-media");
        var cacheDirectory = Path.Combine(_tempDirectory, "pce-takeover-cache");
        Directory.CreateDirectory(mediaDirectory);
        Directory.CreateDirectory(cacheDirectory);

        var mediaPath = Path.Combine(mediaDirectory, "managed-movie.mkv");
        var sourcePath = Path.Combine(cacheDirectory, "movie-4-stream-0-eng.srt");
        await File.WriteAllTextAsync(mediaPath, "clean committed container");
        await File.WriteAllTextAsync(sourcePath, CreateSrtSubtitle("Hello"));
        var fingerprint = BuildFallbackFingerprint(sourcePath, "en", "pl", ".srt");

        var movie = new Movie
        {
            RadarrId = 4,
            Title = "PCE takeover movie",
            FileName = Path.GetFileName(mediaPath),
            Path = mediaDirectory,
            DateAdded = DateTime.UtcNow
        };
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var request = new TranslationRequest
        {
            MediaId = movie.Id,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            SourceSubtitleFormat = ".srt",
            SubtitleOutputMode = "srt",
            RequiredOutputFormats = ".srt",
            SourceSnapshotFingerprint = fingerprint,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Completed,
            WorkloadKind = TranslationWorkloadKind.Library,
            GeneratedSubtitlePaths = System.Text.Json.JsonSerializer.Serialize(
                new[] { $"mkv-embedded:{mediaPath}" })
        };
        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var checkpointService = new Mock<ITranslationCheckpointService>();
        var cacheService = new Mock<IEmbeddedSubtitleCacheService>();
        cacheService
            .Setup(service => service.IsManagedCachePath(sourcePath))
            .Returns(true);

        var mkvEmbeddingService = new Mock<IMkvEmbeddingService>();
        mkvEmbeddingService
            .Setup(service => service.WouldExceedPathLimit(It.IsAny<string>()))
            .Returns(false);
        mkvEmbeddingService
            .Setup(service => service.EmbedSubtitlesAsync(
                mediaPath,
                It.IsAny<IReadOnlyCollection<MkvSubtitleInput>>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, IReadOnlyCollection<MkvSubtitleInput>, CancellationToken>(async (path, _, _) =>
            {
                // Another worker reclaims the request mid-embed, then the embed fails.
                await _dbContext.TranslationRequests
                    .Where(item => item.Id == request.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.JobId, "replacement-claim")
                        .SetProperty(item => item.UpdatedAt, DateTime.UtcNow));
                await File.WriteAllTextAsync(path, "replacement container");
                return new MkvEmbedResult(false, Error: "mkvmerge unavailable");
            });

        var service = CreateService(
            sourcePath,
            checkpointService.Object,
            new Dictionary<string, string>
            {
                [SettingKeys.Translation.RemoveLanguageTag] = "false",
                [SettingKeys.Translation.StripSubtitleFormatting] = "false",
                [SettingKeys.Translation.SubtitleOutputMode] = "srt",
                [SettingKeys.Translation.EmbedInContainer] = "true"
            },
            mkvEmbeddingService: mkvEmbeddingService.Object,
            embeddedSubtitleCacheService: cacheService.Object);

        var result = await service.PublishCompletedEditsAsync(
            request,
            sourcePath,
            new List<SubtitleItem>
            {
                new()
                {
                    Position = 1,
                    StartTime = 0,
                    EndTime = 1000,
                    Lines = new List<string> { "Hello" },
                    PlaintextLines = new List<string> { "Hello" },
                    TranslatedLines = new List<string> { "Czesc" }
                }
            },
            CancellationToken.None);

        // The claim was taken over: this attempt must NOT roll the container back
        // (the replacement worker owns the files), and its rollback backups must
        // not survive as live manifests.
        Assert.False(result.Completed);
        Assert.Contains("mkvmerge unavailable", result.SkippedReason);
        Assert.Equal("replacement container", await File.ReadAllTextAsync(mediaPath));
        Assert.False(
            Directory.EnumerateFiles(_tempDirectory, "*.meta.json", SearchOption.AllDirectories)
                .Any());
    }

    [Fact]
    public async Task PublishCompletedEditsAsync_WhenEmbeddingFailsAfterCommitElsewhere_DoesNotRollBackCommittedContainer()
    {
        var mediaDirectory = Path.Combine(_tempDirectory, "pce-committed-elsewhere-media");
        var cacheDirectory = Path.Combine(_tempDirectory, "pce-committed-elsewhere-cache");
        Directory.CreateDirectory(mediaDirectory);
        Directory.CreateDirectory(cacheDirectory);

        var mediaPath = Path.Combine(mediaDirectory, "managed-movie.mkv");
        var sourcePath = Path.Combine(cacheDirectory, "movie-5-stream-0-eng.srt");
        await File.WriteAllTextAsync(mediaPath, "clean committed container");
        await File.WriteAllTextAsync(sourcePath, CreateSrtSubtitle("Hello"));
        var fingerprint = BuildFallbackFingerprint(sourcePath, "en", "pl", ".srt");

        var movie = new Movie
        {
            RadarrId = 5,
            Title = "PCE committed elsewhere movie",
            FileName = Path.GetFileName(mediaPath),
            Path = mediaDirectory,
            DateAdded = DateTime.UtcNow
        };
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var request = new TranslationRequest
        {
            MediaId = movie.Id,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            SourceSubtitleFormat = ".srt",
            SubtitleOutputMode = "srt",
            RequiredOutputFormats = ".srt",
            SourceSnapshotFingerprint = fingerprint,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Completed,
            WorkloadKind = TranslationWorkloadKind.Library,
            GeneratedSubtitlePaths = System.Text.Json.JsonSerializer.Serialize(
                new[] { $"mkv-embedded:{mediaPath}" })
        };
        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var checkpointService = new Mock<ITranslationCheckpointService>();
        var cacheService = new Mock<IEmbeddedSubtitleCacheService>();
        cacheService
            .Setup(service => service.IsManagedCachePath(sourcePath))
            .Returns(true);

        var mkvEmbeddingService = new Mock<IMkvEmbeddingService>();
        mkvEmbeddingService
            .Setup(service => service.WouldExceedPathLimit(It.IsAny<string>()))
            .Returns(false);
        mkvEmbeddingService
            .Setup(service => service.EmbedSubtitlesAsync(
                mediaPath,
                It.IsAny<IReadOnlyCollection<MkvSubtitleInput>>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, IReadOnlyCollection<MkvSubtitleInput>, CancellationToken>(async (path, _, _) =>
            {
                // Another worker commits the request (JobId nulled, divergent paths
                // so the commit probe misses) mid-embed, then this embed fails.
                var otherWorkerPaths = System.Text.Json.JsonSerializer.Serialize(
                    new[] { "other-worker-output.pl.srt" });
                await _dbContext.TranslationRequests
                    .Where(item => item.Id == request.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.JobId, (string?)null)
                        .SetProperty(item => item.UpdatedAt, DateTime.UtcNow)
                        .SetProperty(item => item.TranslatedSubtitle, "other-worker-output.pl.srt")
                        .SetProperty(item => item.GeneratedSubtitlePaths, otherWorkerPaths));
                await File.WriteAllTextAsync(path, "replacement committed container");
                return new MkvEmbedResult(false, Error: "mkvmerge unavailable");
            });

        var service = CreateService(
            sourcePath,
            checkpointService.Object,
            new Dictionary<string, string>
            {
                [SettingKeys.Translation.RemoveLanguageTag] = "false",
                [SettingKeys.Translation.StripSubtitleFormatting] = "false",
                [SettingKeys.Translation.SubtitleOutputMode] = "srt",
                [SettingKeys.Translation.EmbedInContainer] = "true"
            },
            mkvEmbeddingService: mkvEmbeddingService.Object,
            embeddedSubtitleCacheService: cacheService.Object);

        var result = await service.PublishCompletedEditsAsync(
            request,
            sourcePath,
            new List<SubtitleItem>
            {
                new()
                {
                    Position = 1,
                    StartTime = 0,
                    EndTime = 1000,
                    Lines = new List<string> { "Hello" },
                    PlaintextLines = new List<string> { "Hello" },
                    TranslatedLines = new List<string> { "Czesc" }
                }
            },
            CancellationToken.None);

        // The request was committed elsewhere (JobId null, divergent paths): this
        // attempt must NOT roll the shared container back over the committed output.
        Assert.False(result.Completed);
        Assert.Contains("mkvmerge unavailable", result.SkippedReason);
        Assert.Equal("replacement committed container", await File.ReadAllTextAsync(mediaPath));
        Assert.False(
            Directory.EnumerateFiles(_tempDirectory, "*.meta.json", SearchOption.AllDirectories)
                .Any());
    }

    private FailedTranslationCompletionService CreateService(
        string sourcePath,
        ITranslationCheckpointService checkpointService,
        Dictionary<string, string> settings,
        Func<Task>? beforeFinalCommitAsync = null,
        ISubtitleService? subtitleService = null,
        IMkvEmbeddingService? mkvEmbeddingService = null,
        IEmbeddedSubtitleCacheService? embeddedSubtitleCacheService = null,
        LingarrDbContext? dbContext = null)
    {
        var sourceResolver = new Mock<ISourceSubtitleResolver>();
        sourceResolver
            .Setup(service => service.ResolveReadableSourcePathAsync(
                It.IsAny<TranslationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourcePath);

        var settingService = new Mock<ISettingService>();
        settingService
            .Setup(service => service.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> keys) => keys
                .Where(settings.ContainsKey)
                .ToDictionary(key => key, key => settings[key]));

        var requestService = new Mock<ITranslationRequestService>();
        requestService
            .Setup(service => service.UpdateActiveCount())
            .ReturnsAsync(0);

        var progressService = new Mock<IProgressService>();
        progressService
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        return new FailedTranslationCompletionService(
            dbContext ?? _dbContext,
            sourceResolver.Object,
            subtitleService ?? new SubtitleService(NullLogger<SubtitleService>.Instance),
            checkpointService,
            settingService.Object,
            requestService.Object,
            progressService.Object,
            Mock.Of<IMediaStateService>(),
            NullLogger<FailedTranslationCompletionService>.Instance,
            beforeFinalCommitAsync,
            mkvEmbeddingService,
            embeddedSubtitleCacheService);
    }

    private static string CreateAssSubtitle()
    {
        return "[Script Info]\n" +
               "ScriptType: v4.00+\n\n" +
               "[V4+ Styles]\n" +
               "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n" +
               "Style: Default,Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,1,0,2,10,10,10,1\n\n" +
               "[Events]\n" +
               "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
               "Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,{\\an8}{\\t(0,500,\\fs30)}{\\k20}Hello{\\k30} world\n" +
               "Dialogue: 0,0:00:03.00,0:00:04.00,Default,,0,0,0,,{\\p1}m 0 0 l 10 10{\\p0}\n" +
               "Dialogue: 0,0:00:05.00,0:00:06.00,Default,,0,0,0,,Echo source text\n";
    }

    private static string CreateSrtSubtitle(params string[] lines)
    {
        return string.Join(
            "\n\n",
            lines.Select((line, index) =>
                $"{index + 1}\n00:00:{index + 1:00},000 --> 00:00:{index + 2:00},000\n{line}"));
    }

    private static string BuildFallbackFingerprint(
        string sourcePath,
        string sourceLanguage,
        string targetLanguage,
        string sourceFormat)
    {
        using var stream = File.OpenRead(sourcePath);
        var contentHash = Convert.ToHexString(SHA256.HashData(stream));
        return $"{sourcePath}|{sourceLanguage}|{targetLanguage}|{sourceFormat}|content-sha256:{contentHash}";
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Close();
        _connection.Dispose();

        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
