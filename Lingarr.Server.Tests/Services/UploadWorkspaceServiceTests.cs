using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models;
using Lingarr.Server.Models.UploadWorkspace;
using Lingarr.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class UploadWorkspaceServiceTests
{
    [Fact]
    public async Task StartBatchAsync_CreatesUploadTranslationRequests()
    {
        await using var context = BuildContext();

        var batch = new UploadBatch
        {
            Name = "Upload Batch",
            TargetLanguage = "pl",
            StoragePath = "/uploads/batch-1",
            Status = UploadBatchStatus.Ready
        };

        var uploadFile = new UploadBatchFile
        {
            UploadBatch = batch,
            UploadBatchId = batch.Id,
            FileKind = UploadBatchFileKind.Subtitle,
            Status = UploadBatchFileStatus.Ready,
            Title = "Episode 01",
            OriginalFileName = "Episode.01.en.srt",
            StoredPath = "/uploads/batch-1/originals/Episode.01.en.srt",
            RelativeStoredPath = "originals/Episode.01.en.srt",
            FileSizeBytes = 1024,
            SelectedSourceLanguage = "en"
        };

        batch.Files.Add(uploadFile);
        context.UploadBatches.Add(batch);
        await context.SaveChangesAsync();

        TranslateAbleSubtitle? capturedSubtitle = null;
        var translationRequestServiceMock = new Mock<ITranslationRequestService>();
        translationRequestServiceMock
            .Setup(service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), true))
            .Callback<TranslateAbleSubtitle, bool>((subtitle, _) => capturedSubtitle = subtitle)
            .ReturnsAsync(1234);

        var service = new UploadWorkspaceService(
            context,
            Mock.Of<ISettingService>(),
            Mock.Of<ISubtitleService>(),
            Mock.Of<ISubtitleExtractionService>(),
            new Lazy<ITranslationRequestService>(() => translationRequestServiceMock.Object),
            NullLogger<UploadWorkspaceService>.Instance);

        var queuedCount = await service.StartBatchAsync(batch.Id);

        Assert.Equal(1, queuedCount);

        var updatedFile = await context.UploadBatchFiles.SingleAsync(item => item.Id == uploadFile.Id);
        Assert.Equal(UploadBatchFileStatus.Queued, updatedFile.Status);
        Assert.Equal(1234, updatedFile.CurrentTranslationRequestId);

        Assert.NotNull(capturedSubtitle);
        Assert.Equal(TranslationWorkloadKind.Upload, capturedSubtitle!.WorkloadKind);
        Assert.Equal(uploadFile.Id, capturedSubtitle.UploadBatchFileId);
        Assert.Equal(uploadFile.Id, capturedSubtitle.MediaId);
        Assert.Equal("en", capturedSubtitle.SourceLanguage);
        Assert.Equal("pl", capturedSubtitle.TargetLanguage);

        translationRequestServiceMock.Verify(
            service => service.CreateRequest(
                It.Is<TranslateAbleSubtitle>(subtitle =>
                    subtitle.WorkloadKind == TranslationWorkloadKind.Upload &&
                    subtitle.UploadBatchFileId == uploadFile.Id),
                true),
            Times.Once);
    }

    [Fact]
    public async Task StartBatchAsync_DoesNotQueueAnyFilesWhenAnyQueueableFileIsInvalid()
    {
        await using var context = BuildContext();

        var batch = new UploadBatch
        {
            Name = "Upload Batch",
            TargetLanguage = "pl",
            StoragePath = "/uploads/batch-2",
            Status = UploadBatchStatus.Ready
        };

        var validFile = new UploadBatchFile
        {
            UploadBatch = batch,
            UploadBatchId = batch.Id,
            FileKind = UploadBatchFileKind.Subtitle,
            Status = UploadBatchFileStatus.Ready,
            Title = "Episode 01",
            OriginalFileName = "Episode.01.en.srt",
            StoredPath = "/uploads/batch-2/originals/Episode.01.en.srt",
            RelativeStoredPath = "originals/Episode.01.en.srt",
            FileSizeBytes = 1024,
            SelectedSourceLanguage = "en"
        };

        var invalidFile = new UploadBatchFile
        {
            UploadBatch = batch,
            UploadBatchId = batch.Id,
            FileKind = UploadBatchFileKind.Subtitle,
            Status = UploadBatchFileStatus.Ready,
            Title = "Episode 02",
            OriginalFileName = "Episode.02.unknown.srt",
            StoredPath = "/uploads/batch-2/originals/Episode.02.unknown.srt",
            RelativeStoredPath = "originals/Episode.02.unknown.srt",
            FileSizeBytes = 1024
        };

        batch.Files.Add(validFile);
        batch.Files.Add(invalidFile);
        context.UploadBatches.Add(batch);
        await context.SaveChangesAsync();

        var translationRequestServiceMock = new Mock<ITranslationRequestService>();
        translationRequestServiceMock
            .Setup(service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), true))
            .ReturnsAsync(4321);

        var service = new UploadWorkspaceService(
            context,
            Mock.Of<ISettingService>(),
            Mock.Of<ISubtitleService>(),
            Mock.Of<ISubtitleExtractionService>(),
            new Lazy<ITranslationRequestService>(() => translationRequestServiceMock.Object),
            NullLogger<UploadWorkspaceService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartBatchAsync(batch.Id));

        var updatedFiles = await context.UploadBatchFiles
            .OrderBy(item => item.OriginalFileName)
            .ToListAsync();

        Assert.All(updatedFiles, file => Assert.Equal(UploadBatchFileStatus.Ready, file.Status));
        Assert.All(updatedFiles, file => Assert.Null(file.CurrentTranslationRequestId));
        translationRequestServiceMock.Verify(
            service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), true),
            Times.Never);
    }

    [Fact]
    public async Task StartBatchAsync_DoesNotPersistQueuedFileState_WhenRequestCreationFailsMidBatch()
    {
        await using var context = BuildContext();

        var batch = new UploadBatch
        {
            Name = "Upload Batch",
            TargetLanguage = "pl",
            StoragePath = "/uploads/batch-mid-failure",
            Status = UploadBatchStatus.Ready
        };

        var firstFile = new UploadBatchFile
        {
            UploadBatch = batch,
            UploadBatchId = batch.Id,
            FileKind = UploadBatchFileKind.Subtitle,
            Status = UploadBatchFileStatus.Ready,
            Title = "Episode 01",
            OriginalFileName = "Episode.01.en.srt",
            StoredPath = "/uploads/batch-mid-failure/originals/Episode.01.en.srt",
            RelativeStoredPath = "originals/Episode.01.en.srt",
            FileSizeBytes = 1024,
            SelectedSourceLanguage = "en"
        };

        var secondFile = new UploadBatchFile
        {
            UploadBatch = batch,
            UploadBatchId = batch.Id,
            FileKind = UploadBatchFileKind.Subtitle,
            Status = UploadBatchFileStatus.Ready,
            Title = "Episode 02",
            OriginalFileName = "Episode.02.en.srt",
            StoredPath = "/uploads/batch-mid-failure/originals/Episode.02.en.srt",
            RelativeStoredPath = "originals/Episode.02.en.srt",
            FileSizeBytes = 1024,
            SelectedSourceLanguage = "en"
        };

        batch.Files.Add(firstFile);
        batch.Files.Add(secondFile);
        context.UploadBatches.Add(batch);
        await context.SaveChangesAsync();

        var translationRequestServiceMock = new Mock<ITranslationRequestService>();
        translationRequestServiceMock
            .SetupSequence(service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), true))
            .ReturnsAsync(3001)
            .ThrowsAsync(new InvalidOperationException("Queue failure"));

        var service = new UploadWorkspaceService(
            context,
            Mock.Of<ISettingService>(),
            Mock.Of<ISubtitleService>(),
            Mock.Of<ISubtitleExtractionService>(),
            new Lazy<ITranslationRequestService>(() => translationRequestServiceMock.Object),
            NullLogger<UploadWorkspaceService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartBatchAsync(batch.Id));

        var filesAfterFailure = await context.UploadBatchFiles
            .OrderBy(item => item.OriginalFileName)
            .ToListAsync();

        Assert.All(filesAfterFailure, file => Assert.Equal(UploadBatchFileStatus.Ready, file.Status));
        Assert.All(filesAfterFailure, file => Assert.Null(file.CurrentTranslationRequestId));
    }

    [Fact]
    public async Task UploadFilesAsync_SanitizesOriginalFileNameOnIngestion()
    {
        await using var context = BuildContext();

        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"lingarr-upload-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var settingServiceMock = new Mock<ISettingService>();
            settingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.UploadWorkspace.StorageRoot))
                .ReturnsAsync(workspaceRoot);
            settingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.UploadWorkspace.RetentionDays))
                .ReturnsAsync("7");
            settingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.UploadWorkspace.MaxBatchSize))
                .ReturnsAsync("100");
            settingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.UploadWorkspace.MaxFileSizeBytes))
                .ReturnsAsync((2L * 1024 * 1024).ToString());

            var subtitleServiceMock = new Mock<ISubtitleService>();
            subtitleServiceMock
                .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
                .ReturnsAsync(new List<Subtitles>());

            var service = new UploadWorkspaceService(
                context,
                settingServiceMock.Object,
                subtitleServiceMock.Object,
                Mock.Of<ISubtitleExtractionService>(),
                new Lazy<ITranslationRequestService>(() => Mock.Of<ITranslationRequestService>()),
                NullLogger<UploadWorkspaceService>.Instance);

            var batch = await service.CreateBatchAsync(new CreateUploadBatchRequest
            {
                Name = "Batch",
                TargetLanguage = "pl"
            });

            await using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
            var upload = new FormFile(stream, 0, stream.Length, "files", "..\\..\\nested\\evil.en.srt");
            upload.Headers = new HeaderDictionary();
            upload.ContentType = "application/x-subrip";

            var updatedBatch = await service.UploadFilesAsync(batch.Id, new List<IFormFile> { upload });
            var uploadedFile = Assert.Single(updatedBatch!.Files);

            Assert.Equal("evil.en.srt", uploadedFile.OriginalFileName);
            Assert.StartsWith(Path.Combine(batch.StoragePath, "originals"), uploadedFile.StoredPath, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(uploadedFile.StoredPath));
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UploadFilesAsync_AssignsUniqueStoredPathsForDuplicateSanitizedNames()
    {
        await using var context = BuildContext();

        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"lingarr-upload-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var settingServiceMock = new Mock<ISettingService>();
            settingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.UploadWorkspace.StorageRoot))
                .ReturnsAsync(workspaceRoot);
            settingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.UploadWorkspace.RetentionDays))
                .ReturnsAsync("7");
            settingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.UploadWorkspace.MaxBatchSize))
                .ReturnsAsync("100");
            settingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.UploadWorkspace.MaxFileSizeBytes))
                .ReturnsAsync((2L * 1024 * 1024).ToString());

            var subtitleServiceMock = new Mock<ISubtitleService>();
            subtitleServiceMock
                .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
                .ReturnsAsync(new List<Subtitles>());

            var service = new UploadWorkspaceService(
                context,
                settingServiceMock.Object,
                subtitleServiceMock.Object,
                Mock.Of<ISubtitleExtractionService>(),
                new Lazy<ITranslationRequestService>(() => Mock.Of<ITranslationRequestService>()),
                NullLogger<UploadWorkspaceService>.Instance);

            var batch = await service.CreateBatchAsync(new CreateUploadBatchRequest
            {
                Name = "Batch",
                TargetLanguage = "pl"
            });

            await using var firstStream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
            await using var secondStream = new MemoryStream(new byte[] { 5, 6, 7, 8 });
            var firstUpload = new FormFile(firstStream, 0, firstStream.Length, "files", "..\\nested\\episode.en.srt");
            var secondUpload = new FormFile(secondStream, 0, secondStream.Length, "files", "episode.en.srt");
            firstUpload.Headers = new HeaderDictionary();
            secondUpload.Headers = new HeaderDictionary();
            firstUpload.ContentType = "application/x-subrip";
            secondUpload.ContentType = "application/x-subrip";

            var updatedBatch = await service.UploadFilesAsync(batch.Id, new List<IFormFile> { firstUpload, secondUpload });

            Assert.NotNull(updatedBatch);
            Assert.Equal(2, updatedBatch.Files.Count);
            Assert.All(updatedBatch.Files, file => Assert.Equal("episode.en.srt", file.OriginalFileName));
            Assert.Equal(2, updatedBatch.Files.Select(file => file.StoredPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Contains(updatedBatch.Files, file => Path.GetFileName(file.StoredPath) == "episode.en.srt");
            Assert.Contains(updatedBatch.Files, file => Path.GetFileName(file.StoredPath) == "episode.en_1.srt");
            Assert.All(updatedBatch.Files, file => Assert.True(File.Exists(file.StoredPath)));
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UpdateFileAsync_ClearsSelectedEmbeddedStream_WhenNullProvided()
    {
        await using var context = BuildContext();

        var batch = new UploadBatch
        {
            Name = "Upload Batch",
            TargetLanguage = "pl",
            StoragePath = "/uploads/batch-4",
            Status = UploadBatchStatus.Ready
        };

        var uploadFile = new UploadBatchFile
        {
            UploadBatch = batch,
            UploadBatchId = batch.Id,
            FileKind = UploadBatchFileKind.Media,
            Status = UploadBatchFileStatus.Ready,
            Title = "Episode 01",
            OriginalFileName = "Episode.01.mkv",
            StoredPath = "/uploads/batch-4/originals/Episode.01.mkv",
            RelativeStoredPath = "originals/Episode.01.mkv",
            FileSizeBytes = 1024,
            SelectedSourceLanguage = "en",
            SelectedEmbeddedStreamIndex = 2,
            SelectedEmbeddedStreamLanguage = "en",
            SelectedEmbeddedStreamTitle = "English",
            SelectedEmbeddedStreamCodec = "srt"
        };

        uploadFile.SubtitleStreams.Add(new UploadBatchFileSubtitleStream
        {
            UploadBatchFile = uploadFile,
            UploadBatchFileId = uploadFile.Id,
            StreamIndex = 2,
            Language = "en",
            Title = "English",
            CodecName = "srt",
            IsTextBased = true
        });

        batch.Files.Add(uploadFile);
        context.UploadBatches.Add(batch);
        await context.SaveChangesAsync();

        var service = new UploadWorkspaceService(
            context,
            Mock.Of<ISettingService>(),
            Mock.Of<ISubtitleService>(),
            Mock.Of<ISubtitleExtractionService>(),
            new Lazy<ITranslationRequestService>(() => Mock.Of<ITranslationRequestService>()),
            NullLogger<UploadWorkspaceService>.Instance);

        var updatedFile = await service.UpdateFileAsync(batch.Id, uploadFile.Id, new UpdateUploadBatchFileRequest
        {
            SelectedSourceLanguage = "en",
            SelectedEmbeddedStreamIndex = ParseJsonElement("null")
        });

        Assert.NotNull(updatedFile);
        Assert.Null(updatedFile!.SelectedEmbeddedStreamIndex);
        Assert.Null(updatedFile.SelectedEmbeddedStreamLanguage);
        Assert.Null(updatedFile.SelectedEmbeddedStreamTitle);
        Assert.Null(updatedFile.SelectedEmbeddedStreamCodec);
    }

    [Fact]
    public async Task UpdateFileAsync_PreservesSelectedEmbeddedStream_WhenFieldOmitted()
    {
        await using var context = BuildContext();

        var batch = new UploadBatch
        {
            Name = "Upload Batch",
            TargetLanguage = "pl",
            StoragePath = "/uploads/batch-5",
            Status = UploadBatchStatus.Ready
        };

        var uploadFile = new UploadBatchFile
        {
            UploadBatch = batch,
            UploadBatchId = batch.Id,
            FileKind = UploadBatchFileKind.Media,
            Status = UploadBatchFileStatus.Ready,
            Title = "Episode 01",
            OriginalFileName = "Episode.01.mkv",
            StoredPath = "/uploads/batch-5/originals/Episode.01.mkv",
            RelativeStoredPath = "originals/Episode.01.mkv",
            FileSizeBytes = 1024,
            SelectedSourceLanguage = "en",
            SelectedEmbeddedStreamIndex = 3,
            SelectedEmbeddedStreamLanguage = "en",
            SelectedEmbeddedStreamTitle = "English Full",
            SelectedEmbeddedStreamCodec = "ass"
        };

        uploadFile.SubtitleStreams.Add(new UploadBatchFileSubtitleStream
        {
            UploadBatchFile = uploadFile,
            UploadBatchFileId = uploadFile.Id,
            StreamIndex = 3,
            Language = "en",
            Title = "English Full",
            CodecName = "ass",
            IsTextBased = true
        });

        batch.Files.Add(uploadFile);
        context.UploadBatches.Add(batch);
        await context.SaveChangesAsync();

        var service = new UploadWorkspaceService(
            context,
            Mock.Of<ISettingService>(),
            Mock.Of<ISubtitleService>(),
            Mock.Of<ISubtitleExtractionService>(),
            new Lazy<ITranslationRequestService>(() => Mock.Of<ITranslationRequestService>()),
            NullLogger<UploadWorkspaceService>.Instance);

        var updatedFile = await service.UpdateFileAsync(batch.Id, uploadFile.Id, new UpdateUploadBatchFileRequest
        {
            SelectedSourceLanguage = "en"
        });

        Assert.NotNull(updatedFile);
        Assert.Equal(3, updatedFile!.SelectedEmbeddedStreamIndex);
        Assert.Equal("en", updatedFile.SelectedEmbeddedStreamLanguage);
        Assert.Equal("English Full", updatedFile.SelectedEmbeddedStreamTitle);
        Assert.Equal("ass", updatedFile.SelectedEmbeddedStreamCodec);
    }

    [Fact]
    public async Task GetOutputPathsAsync_RejectsPathsOutsideBatchStorage()
    {
        await using var context = BuildContext();

        var workspaceRoot = Path.Combine(AppContext.BaseDirectory, "testdata", $"lingarr-upload-root-{Guid.NewGuid():N}");
        var outsideRoot = workspaceRoot + "-outside";

        var batch = new UploadBatch
        {
            Name = "Upload Batch",
            TargetLanguage = "pl",
            StoragePath = Path.Combine(workspaceRoot, "batch-6"),
            Status = UploadBatchStatus.Ready
        };

        var uploadFile = new UploadBatchFile
        {
            UploadBatch = batch,
            UploadBatchId = batch.Id,
            FileKind = UploadBatchFileKind.Subtitle,
            Status = UploadBatchFileStatus.Ready,
            Title = "Episode 01",
            OriginalFileName = "Episode.01.en.srt",
            StoredPath = Path.Combine(batch.StoragePath, "originals", "Episode.01.en.srt"),
            RelativeStoredPath = "originals/Episode.01.en.srt",
            FileSizeBytes = 1024,
            SelectedSourceLanguage = "en"
        };

        batch.Files.Add(uploadFile);
        context.UploadBatches.Add(batch);
        await context.SaveChangesAsync();

        var subtitleServiceMock = new Mock<ISubtitleService>();
        subtitleServiceMock
            .Setup(service => service.CreateFallbackPaths(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(new List<string>
            {
                Path.Combine(batch.StoragePath, "translated", "Episode.01.pl.srt"),
                Path.Combine(outsideRoot, "escape.pl.srt")
            });

        var service = new UploadWorkspaceService(
            context,
            Mock.Of<ISettingService>(),
            subtitleServiceMock.Object,
            Mock.Of<ISubtitleExtractionService>(),
            new Lazy<ITranslationRequestService>(() => Mock.Of<ITranslationRequestService>()),
            NullLogger<UploadWorkspaceService>.Instance);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetOutputPathsAsync(
                new TranslationRequest
                {
                    UploadBatchFileId = uploadFile.Id,
                    Title = "Episode 01",
                    SourceLanguage = "en",
                    TargetLanguage = "pl",
                    MediaType = MediaType.Movie,
                    Status = TranslationStatus.Pending
                },
                "pl",
                "lingarr",
                "lnr",
                ".srt"));
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(outsideRoot))
            {
                Directory.Delete(outsideRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DeleteArtifactAsync_RejectsPathOutsideWorkspaceRoot()
    {
        await using var context = BuildContext();

        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"lingarr-upload-root-{Guid.NewGuid():N}");
        var outsideRoot = workspaceRoot + "-outside";
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(outsideRoot);

        var outsideFilePath = Path.Combine(outsideRoot, "danger.srt");
        await File.WriteAllTextAsync(outsideFilePath, "subtitle");

        try
        {
            var batch = new UploadBatch
            {
                Name = "Upload Batch",
                TargetLanguage = "pl",
                StoragePath = Path.Combine(workspaceRoot, "batch-3"),
                Status = UploadBatchStatus.Ready
            };
            context.UploadBatches.Add(batch);
            await context.SaveChangesAsync();

            var artifact = new UploadArtifact
            {
                UploadBatchId = batch.Id,
                UploadBatch = batch,
                Kind = UploadArtifactKind.TranslatedSubtitle,
                FileName = "danger.srt",
                Path = outsideFilePath,
                RelativePath = "../outside/danger.srt",
                FileSizeBytes = 8,
                IsDownloadable = true
            };
            context.UploadArtifacts.Add(artifact);
            await context.SaveChangesAsync();

            var settingServiceMock = new Mock<ISettingService>();
            settingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.UploadWorkspace.StorageRoot))
                .ReturnsAsync(workspaceRoot);

            var service = new UploadWorkspaceService(
                context,
                settingServiceMock.Object,
                Mock.Of<ISubtitleService>(),
                Mock.Of<ISubtitleExtractionService>(),
                new Lazy<ITranslationRequestService>(() => Mock.Of<ITranslationRequestService>()),
                NullLogger<UploadWorkspaceService>.Instance);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteArtifactAsync(artifact.Id));
            Assert.True(File.Exists(outsideFilePath));
            Assert.NotNull(await context.UploadArtifacts.FindAsync(artifact.Id));
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(outsideRoot))
            {
                Directory.Delete(outsideRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UpdateBatchAsync_MarksSubtitleAsNeedsConfiguration_WhenTargetMatchesSourceLanguage()
    {
        await using var context = BuildContext();

        var batch = new UploadBatch
        {
            Name = "Upload Batch",
            TargetLanguage = "pl",
            StoragePath = "/uploads/batch-7",
            Status = UploadBatchStatus.Ready
        };

        var uploadFile = new UploadBatchFile
        {
            UploadBatch = batch,
            UploadBatchId = batch.Id,
            FileKind = UploadBatchFileKind.Subtitle,
            Status = UploadBatchFileStatus.Ready,
            Title = "Episode 01",
            OriginalFileName = "Episode.01.en.srt",
            StoredPath = "/uploads/batch-7/originals/Episode.01.en.srt",
            RelativeStoredPath = "originals/Episode.01.en.srt",
            FileSizeBytes = 1024,
            SelectedSourceLanguage = "en"
        };

        batch.Files.Add(uploadFile);
        context.UploadBatches.Add(batch);
        await context.SaveChangesAsync();

        var service = new UploadWorkspaceService(
            context,
            Mock.Of<ISettingService>(),
            Mock.Of<ISubtitleService>(),
            Mock.Of<ISubtitleExtractionService>(),
            new Lazy<ITranslationRequestService>(() => Mock.Of<ITranslationRequestService>()),
            NullLogger<UploadWorkspaceService>.Instance);

        var updatedBatch = await service.UpdateBatchAsync(batch.Id, new UpdateUploadBatchRequest
        {
            Name = batch.Name,
            TargetLanguage = "en",
            DefaultRemuxEnabled = false
        });

        var updatedFile = Assert.Single(updatedBatch!.Files);
        Assert.Equal(UploadBatchFileStatus.NeedsConfiguration, updatedFile.Status);
        Assert.Equal(
            "Source language cannot match the batch target language. Choose a different source language.",
            updatedFile.ProbeError);
    }

    [Fact]
    public async Task ReprobeFileAsync_UsesConfiguredSourceLanguagePriority_ForEmbeddedStreams()
    {
        await using var context = BuildContext();

        var settingServiceMock = new Mock<ISettingService>();
        settingServiceMock
            .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync(
            [
                new SourceLanguage { Name = "English", Code = "en", Targets = [] },
                new SourceLanguage { Name = "Japanese", Code = "ja", Targets = [] }
            ]);

        var extractionServiceMock = new Mock<ISubtitleExtractionService>();
        extractionServiceMock
            .Setup(service => service.ProbeEmbeddedSubtitles(It.IsAny<string>()))
            .ReturnsAsync(
            [
                new EmbeddedSubtitle
                {
                    StreamIndex = 1,
                    Language = "eng",
                    Title = "Signs & Songs",
                    CodecName = "ass",
                    IsTextBased = true,
                    IsForced = false,
                    IsDefault = false
                },
                new EmbeddedSubtitle
                {
                    StreamIndex = 2,
                    Language = "jpn",
                    Title = "Full",
                    CodecName = "ass",
                    IsTextBased = true,
                    IsForced = false,
                    IsDefault = false
                }
            ]);

        var batch = new UploadBatch
        {
            Name = "Upload Batch",
            TargetLanguage = "pl",
            StoragePath = "/uploads/batch-8",
            Status = UploadBatchStatus.Ready
        };

        var uploadFile = new UploadBatchFile
        {
            UploadBatch = batch,
            UploadBatchId = batch.Id,
            FileKind = UploadBatchFileKind.Media,
            Status = UploadBatchFileStatus.Uploaded,
            Title = "Episode 01",
            OriginalFileName = "Episode.01.mkv",
            StoredPath = "/uploads/batch-8/originals/Episode.01.mkv",
            RelativeStoredPath = "originals/Episode.01.mkv",
            FileSizeBytes = 2048
        };

        batch.Files.Add(uploadFile);
        context.UploadBatches.Add(batch);
        await context.SaveChangesAsync();

        var service = new UploadWorkspaceService(
            context,
            settingServiceMock.Object,
            Mock.Of<ISubtitleService>(),
            extractionServiceMock.Object,
            new Lazy<ITranslationRequestService>(() => Mock.Of<ITranslationRequestService>()),
            NullLogger<UploadWorkspaceService>.Instance);

        var reprobedFile = await service.ReprobeFileAsync(batch.Id, uploadFile.Id);

        Assert.NotNull(reprobedFile);
        Assert.Equal("ja", reprobedFile!.SelectedSourceLanguage);
        Assert.Equal(2, reprobedFile.SelectedEmbeddedStreamIndex);
        Assert.Equal(UploadBatchFileStatus.Ready, reprobedFile.Status);
    }

    [Fact]
    public async Task CreateChunkSessionAsync_CompleteChunkSessionAsync_UsesFilenameFallbackForAssUploads()
    {
        await using var context = BuildContext();

        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"lingarr-upload-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var settingServiceMock = new Mock<ISettingService>();
            settingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.UploadWorkspace.StorageRoot))
                .ReturnsAsync(workspaceRoot);
            settingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.UploadWorkspace.RetentionDays))
                .ReturnsAsync("7");
            settingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.UploadWorkspace.MaxBatchSize))
                .ReturnsAsync("100");
            settingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.UploadWorkspace.MaxFileSizeBytes))
                .ReturnsAsync((20L * 1024 * 1024).ToString());
            settingServiceMock
                .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
                .ReturnsAsync([new SourceLanguage { Name = "English", Code = "en", Targets = [] }]);

            var subtitleServiceMock = new Mock<ISubtitleService>();
            subtitleServiceMock
                .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
                .ReturnsAsync([]);

            var service = new UploadWorkspaceService(
                context,
                settingServiceMock.Object,
                subtitleServiceMock.Object,
                Mock.Of<ISubtitleExtractionService>(),
                new Lazy<ITranslationRequestService>(() => Mock.Of<ITranslationRequestService>()),
                NullLogger<UploadWorkspaceService>.Instance);

            var batch = await service.CreateBatchAsync(new CreateUploadBatchRequest
            {
                Name = "Chunked Batch",
                TargetLanguage = "pl"
            });

            var payload = new byte[11 * 1024 * 1024];
            new Random(1234).NextBytes(payload);

            var session = await service.CreateChunkSessionAsync(batch.Id, new CreateUploadChunkSessionRequest
            {
                FileName = "Movie.en.ass",
                FileSizeBytes = payload.Length,
                ContentType = "text/x-ssa",
                LastModifiedUtc = DateTime.UtcNow
            });

            Assert.NotNull(session);
            Assert.Equal(8 * 1024 * 1024, session!.ChunkSizeBytes);
            Assert.Equal(2, session.ExpectedChunks);

            await using var firstChunk = new MemoryStream(payload, 0, session.ChunkSizeBytes, writable: false);
            var secondChunkLength = payload.Length - session.ChunkSizeBytes;
            await using var secondChunk = new MemoryStream(payload, session.ChunkSizeBytes, secondChunkLength, writable: false);

            await service.UploadChunkAsync(batch.Id, session.UploadId, 0, firstChunk, firstChunk.Length);
            await service.UploadChunkAsync(batch.Id, session.UploadId, 1, secondChunk, secondChunk.Length);

            var completedBatch = await service.CompleteChunkSessionAsync(batch.Id, session.UploadId);
            var uploadedFile = Assert.Single(completedBatch!.Files);

            Assert.Equal(UploadBatchFileKind.Subtitle, uploadedFile.FileKind);
            Assert.Equal("en", uploadedFile.DetectedSourceLanguage);
            Assert.Equal("en", uploadedFile.SelectedSourceLanguage);
            Assert.Equal(UploadBatchFileStatus.Ready, uploadedFile.Status);
            Assert.True(File.Exists(uploadedFile.StoredPath));
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UploadChunkAsync_RejectsWhenCumulativeChunksExceedDeclaredFileSize()
    {
        await using var context = BuildContext();

        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"lingarr-upload-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var settingServiceMock = new Mock<ISettingService>();
            settingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.UploadWorkspace.StorageRoot))
                .ReturnsAsync(workspaceRoot);
            settingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.UploadWorkspace.RetentionDays))
                .ReturnsAsync("7");
            settingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.UploadWorkspace.MaxBatchSize))
                .ReturnsAsync("100");
            settingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.UploadWorkspace.MaxFileSizeBytes))
                .ReturnsAsync((20L * 1024 * 1024).ToString());
            settingServiceMock
                .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
                .ReturnsAsync([new SourceLanguage { Name = "English", Code = "en", Targets = [] }]);

            var subtitleServiceMock = new Mock<ISubtitleService>();
            subtitleServiceMock
                .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
                .ReturnsAsync([]);

            var service = new UploadWorkspaceService(
                context,
                settingServiceMock.Object,
                subtitleServiceMock.Object,
                Mock.Of<ISubtitleExtractionService>(),
                new Lazy<ITranslationRequestService>(() => Mock.Of<ITranslationRequestService>()),
                NullLogger<UploadWorkspaceService>.Instance);

            var batch = await service.CreateBatchAsync(new CreateUploadBatchRequest
            {
                Name = "Oversized Chunk Batch",
                TargetLanguage = "pl"
            });

            var session = await service.CreateChunkSessionAsync(batch.Id, new CreateUploadChunkSessionRequest
            {
                FileName = "Movie.en.ass",
                FileSizeBytes = 128,
                ContentType = "text/x-ssa",
                LastModifiedUtc = DateTime.UtcNow
            });

            await using var firstChunk = new MemoryStream(new byte[96], writable: false);
            await service.UploadChunkAsync(batch.Id, session!.UploadId, 0, firstChunk, firstChunk.Length);

            await using var oversizedChunk = new MemoryStream(new byte[64], writable: false);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.UploadChunkAsync(batch.Id, session.UploadId, 1, oversizedChunk, oversizedChunk.Length));

            Assert.Contains("expected file size", exception.Message, StringComparison.OrdinalIgnoreCase);

            var rejectedChunkPath = Path.Combine(
                batch.StoragePath,
                "incoming",
                session.UploadId.ToString("D"),
                "chunk-000001.part");
            Assert.False(File.Exists(rejectedChunkPath));
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CompleteChunkSessionAsync_RejectsWhenBatchAlreadyReachedMaxSize()
    {
        await using var context = BuildContext();

        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"lingarr-upload-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var settingServiceMock = new Mock<ISettingService>();
            settingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.UploadWorkspace.StorageRoot))
                .ReturnsAsync(workspaceRoot);
            settingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.UploadWorkspace.RetentionDays))
                .ReturnsAsync("7");
            settingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.UploadWorkspace.MaxBatchSize))
                .ReturnsAsync("1");
            settingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.UploadWorkspace.MaxFileSizeBytes))
                .ReturnsAsync((20L * 1024 * 1024).ToString());
            settingServiceMock
                .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
                .ReturnsAsync([new SourceLanguage { Name = "English", Code = "en", Targets = [] }]);

            var subtitleServiceMock = new Mock<ISubtitleService>();
            subtitleServiceMock
                .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
                .ReturnsAsync([]);

            var service = new UploadWorkspaceService(
                context,
                settingServiceMock.Object,
                subtitleServiceMock.Object,
                Mock.Of<ISubtitleExtractionService>(),
                new Lazy<ITranslationRequestService>(() => Mock.Of<ITranslationRequestService>()),
                NullLogger<UploadWorkspaceService>.Instance);

            var batch = await service.CreateBatchAsync(new CreateUploadBatchRequest
            {
                Name = "Chunk Limit Batch",
                TargetLanguage = "pl"
            });

            var session = await service.CreateChunkSessionAsync(batch.Id, new CreateUploadChunkSessionRequest
            {
                FileName = "Movie.en.ass",
                FileSizeBytes = 128,
                ContentType = "text/x-ssa",
                LastModifiedUtc = DateTime.UtcNow
            });

            context.UploadBatchFiles.Add(new UploadBatchFile
            {
                UploadBatchId = batch.Id,
                UploadBatch = batch,
                FileKind = UploadBatchFileKind.Subtitle,
                Status = UploadBatchFileStatus.Uploaded,
                Title = "Existing",
                OriginalFileName = "Existing.en.srt",
                StoredPath = Path.Combine(batch.StoragePath, "originals", "Existing.en.srt"),
                RelativeStoredPath = Path.Combine("originals", "Existing.en.srt"),
                FileSizeBytes = 64,
                SelectedSourceLanguage = "en"
            });
            await context.SaveChangesAsync();

            await using var chunk = new MemoryStream(new byte[128], writable: false);
            await service.UploadChunkAsync(batch.Id, session!.UploadId, 0, chunk, chunk.Length);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CompleteChunkSessionAsync(batch.Id, session.UploadId));

            Assert.Contains("configured batch-size limit", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    private static LingarrDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LingarrDbContext(options);
    }

    private static JsonElement ParseJsonElement(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
}
