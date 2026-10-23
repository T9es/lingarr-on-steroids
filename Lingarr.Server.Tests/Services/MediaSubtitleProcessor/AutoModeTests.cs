using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Subtitle;
using Lingarr.Server.Services.Subtitle;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.MediaSubtitleProcessor;

public class AutoModeTests : MediaSubtitleProcessorTestBase
{
    [Fact]
    public async Task ProcessMediaForceAsync_AutoModeKeepsGeneratedPolishAsTargetAndEmbeddedEnglishAsSource()
    {
        var movie = await CreateTestMovie("auto.feedback.mkv");
        var tempDirectory = Path.Combine(Path.GetTempPath(), "lingarr-auto-feedback-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var sourcePath = Path.Combine(tempDirectory, "embedded.en.srt");
        var targetPath = Path.Combine(tempDirectory, "auto.feedback.pl.srt");
        var generatedContent = CreateSrtContent("Polish output line");
        await File.WriteAllTextAsync(sourcePath, string.Join(
            "\n\n",
            Enumerable.Range(1, 60).Select(index =>
                $"{index}\n00:00:{index % 60:00},000 --> 00:00:{index % 60:00},500\nEnglish source line {index}")));
        await File.WriteAllTextAsync(targetPath, generatedContent);

        try
        {
            var embeddedSubtitle = new EmbeddedSubtitle
            {
                MovieId = movie.Id,
                StreamIndex = 0,
                Language = "eng",
                Title = "English Full",
                CodecName = "subrip",
                IsTextBased = true,
                IsExtracted = true,
                ExtractedPath = sourcePath
            };
            movie.EmbeddedSubtitles.Add(embeddedSubtitle);
            await DbContext.SaveChangesAsync();

            SettingServiceMock
                .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
                .ReturnsAsync([]);
            SettingServiceMock
                .Setup(service => service.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
                .ReturnsAsync([new TargetLanguage { Code = "pl", Name = "Polish" }]);
            SettingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.Translation.SourceLanguageMode))
                .ReturnsAsync("auto");
            SettingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.Translation.IgnoreCaptions))
                .ReturnsAsync("false");
            SettingServiceMock
                .Setup(service => service.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
                .ReturnsAsync("srt-only");

            SubtitleServiceMock
                .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
                .ReturnsAsync([
                    new Subtitles
                    {
                        Path = targetPath,
                        FileName = "auto.feedback.pl.srt",
                        Language = "pl",
                        Format = ".srt"
                    }
                ]);
            SubtitleServiceMock
                .Setup(service => service.ReadSubtitles(sourcePath))
                .ReturnsAsync(Enumerable.Range(1, 60)
                    .Select(index => new SubtitleItem
                    {
                        Position = index,
                        Lines = [$"English source line {index}"],
                        PlaintextLines = [$"English source line {index}"]
                    })
                    .ToList());
            SubtitleExtractionServiceMock
                .Setup(service => service.SyncEmbeddedSubtitles(It.IsAny<Movie>()))
                .Returns(Task.CompletedTask);

            var snapshotService = new SourceSubtitleSnapshotService(
                DbContext,
                SettingServiceMock.Object,
                SubtitleServiceMock.Object,
                NullLogger<SourceSubtitleSnapshotService>.Instance);
            var sourceSnapshot = snapshotService.CreateEmbeddedSnapshot(embeddedSubtitle, "en");
            DbContext.TranslationRequests.Add(new TranslationRequest
            {
                MediaId = movie.Id,
                Title = movie.Title,
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = MediaType.Movie,
                Status = TranslationStatus.Completed,
                WorkloadKind = TranslationWorkloadKind.Library,
                CompletedAt = DateTime.UtcNow.AddMinutes(-1),
                RequiredOutputFormats = ".srt",
                GeneratedOutputFormats = ".srt",
                GeneratedSubtitlePaths = JsonSerializer.Serialize(new[] { targetPath }),
                SourceSnapshotVersion = sourceSnapshot.Version,
                SourceSnapshotType = sourceSnapshot.SourceType,
                SourceSnapshotIdentity = sourceSnapshot.Identity,
                SourceSnapshotFingerprint = sourceSnapshot.Fingerprint,
                SourceSnapshotStreamIndex = sourceSnapshot.StreamIndex
            });
            await DbContext.SaveChangesAsync();

            var qualityScorerMock = new Mock<ITranslationQualityScorer>();
            qualityScorerMock
                .SetupGet(scorer => scorer.MinimumAcceptableScore)
                .Returns(50);
            qualityScorerMock
                .Setup(scorer => scorer.ScoreDirection(It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string sourceLanguage, string targetLanguage) =>
                    sourceLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase) &&
                    targetLanguage.StartsWith("pl", StringComparison.OrdinalIgnoreCase)
                        ? 85
                        : null);

            var selectionService = new SubtitleSourceSelectionService(
                SubtitleServiceMock.Object,
                NullLogger<SubtitleSourceSelectionService>.Instance,
                qualityScorerMock.Object);
            var processor = new Lingarr.Server.Services.MediaSubtitleProcessor(
                TranslationRequestServiceMock.Object,
                LoggerMock.Object,
                SettingServiceMock.Object,
                SubtitleServiceMock.Object,
                SubtitleExtractionServiceMock.Object,
                SubtitleIntegrityServiceMock.Object,
                snapshotService,
                DbContext,
                selectionService,
                SubtitleOcrServiceMock.Object,
                backgroundJobClient: BackgroundJobClientMock.Object);

            var firstQueueCount = await processor.ProcessMediaForceAsync(
                movie,
                MediaType.Movie,
                forceProcess: true,
                forceTranslation: false);

            generatedContent = CreateSrtContent("Edited Polish output line");
            await File.WriteAllTextAsync(targetPath, generatedContent);

            var secondQueueCount = await processor.ProcessMediaForceAsync(
                movie,
                MediaType.Movie,
                forceProcess: true,
                forceTranslation: false);

            Assert.Equal(0, firstQueueCount);
            Assert.Equal(0, secondQueueCount);
            Assert.Equal(generatedContent, await File.ReadAllTextAsync(targetPath));
            Assert.Equal(sourceSnapshot.Fingerprint, DbContext.TranslationRequests.Single().SourceSnapshotFingerprint);
            TranslationRequestServiceMock.Verify(
                service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()),
                Times.Never);
            qualityScorerMock.Verify(
                scorer => scorer.ScoreDirection("en", "pl"),
                Times.AtLeastOnce);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }

        static string CreateSrtContent(string linePrefix)
        {
            return string.Join(
                "\n\n",
                Enumerable.Range(1, 60).Select(index =>
                {
                    var start = TimeSpan.FromSeconds(index * 2);
                    var end = start.Add(TimeSpan.FromSeconds(1));
                    return $"{index}\n{start.ToString(@"hh\:mm\:ss\,fff")} --> {end.ToString(@"hh\:mm\:ss\,fff")}\n{linePrefix} {index}";
                }));
        }
    }

    [Fact]
    public async Task ProcessMediaForceAsync_AutoReportOnlyExcludesSelfLanguageTarget()
    {
        var movie = await CreateTestMovie("auto.report.mkv");
        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync([]);
        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync([
                new TargetLanguage { Code = "en", Name = "English" },
                new TargetLanguage { Code = "pl", Name = "Polish" }
            ]);
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SourceLanguageMode))
            .ReturnsAsync("auto");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("false");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync("srt-only");
        SubtitleServiceMock
            .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync([
                new Subtitles
                {
                    Path = "/movies/test/auto.report.en.srt",
                    FileName = "auto.report.en.srt",
                    Language = "en",
                    Format = ".srt"
                }
            ]);

        var reportedCount = await Processor.ProcessMediaForceAsync(
            movie,
            MediaType.Movie,
            forceProcess: true,
            forceTranslation: true,
            queueTranslations: false);

        Assert.Equal(1, reportedCount);
        TranslationRequestServiceMock.Verify(
            service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessMediaForceAsync_AutoSupplementalSourceSkipsOnlyItsSelfTargetBeforeQueueLimit()
    {
        var movie = await CreateTestMovie("auto.supplemental.mkv");
        var primarySubtitle = new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 0,
            Language = "en",
            Title = "English Full",
            CodecName = "subrip",
            IsTextBased = true
        };
        var supplementalSubtitle = new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 1,
            Language = "pl",
            Title = "Signs",
            CodecName = "subrip",
            IsTextBased = true,
            IsForced = true
        };
        movie.EmbeddedSubtitles.AddRange([primarySubtitle, supplementalSubtitle]);
        DbContext.EmbeddedSubtitles.AddRange(primarySubtitle, supplementalSubtitle);
        await DbContext.SaveChangesAsync();

        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync([]);
        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync([
                new TargetLanguage { Code = "pl", Name = "Polish" },
                new TargetLanguage { Code = "de", Name = "German" }
            ]);
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SourceLanguageMode))
            .ReturnsAsync("auto");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("false");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync("srt-only");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.SubtitleValidation.SkipWhenTargetEmbedded))
            .ReturnsAsync("false");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.TranslateSupplementalSubtitles))
            .ReturnsAsync("true");
        SubtitleServiceMock
            .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync([]);
        SubtitleExtractionServiceMock
            .Setup(service => service.SyncEmbeddedSubtitles(It.IsAny<Movie>()))
            .Returns(Task.CompletedTask);

        var sourceSelection = new SubtitleSourceSelectionResult
        {
            SelectedSubtitle = primarySubtitle,
            MatchedLanguage = "en",
            SelectedRole = SubtitleSourceCandidateRole.PrimaryFullDialogue,
            Assessments =
            [
                new SubtitleSourceCandidateAssessment(
                    primarySubtitle,
                    SubtitleSourceCandidateRole.PrimaryFullDialogue,
                    "en",
                    90,
                    60,
                    "primary"),
                new SubtitleSourceCandidateAssessment(
                    supplementalSubtitle,
                    SubtitleSourceCandidateRole.SupplementalForcedSigns,
                    "pl",
                    80,
                    60,
                    "supplemental")
            ]
        };
        var sourceSelectionServiceMock = new Mock<ISubtitleSourceSelectionService>();
        sourceSelectionServiceMock
            .Setup(service => service.SelectPrimaryAsync(
                It.IsAny<IReadOnlyCollection<EmbeddedSubtitle>>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<bool>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<double?>()))
            .ReturnsAsync(sourceSelection);

        var queuedRequests = new List<TranslateAbleSubtitle>();
        TranslationRequestServiceMock
            .Setup(service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()))
            .Callback<TranslateAbleSubtitle, bool>((request, _) => queuedRequests.Add(request))
            .ReturnsAsync(123);
        var processor = new Lingarr.Server.Services.MediaSubtitleProcessor(
            TranslationRequestServiceMock.Object,
            LoggerMock.Object,
            SettingServiceMock.Object,
            SubtitleServiceMock.Object,
            SubtitleExtractionServiceMock.Object,
            SubtitleIntegrityServiceMock.Object,
            SourceSubtitleSnapshotServiceMock.Object,
            DbContext,
            subtitleSourceSelectionService: sourceSelectionServiceMock.Object,
            subtitleOcrService: SubtitleOcrServiceMock.Object,
            backgroundJobClient: BackgroundJobClientMock.Object);

        var queuedCount = await processor.ProcessMediaForceAsync(
            movie,
            MediaType.Movie,
            forceProcess: true,
            forceTranslation: false,
            maxTranslationsToQueue: 3);

        Assert.Equal(3, queuedCount);
        Assert.Contains(queuedRequests, request => request.SourceLanguage == "en" && request.TargetLanguage == "pl");
        Assert.Contains(queuedRequests, request => request.SourceLanguage == "en" && request.TargetLanguage == "de");
        Assert.Contains(queuedRequests, request => request.SourceLanguage == "pl" && request.TargetLanguage == "de");
        Assert.DoesNotContain(queuedRequests, request => request.SourceLanguage == "pl" && request.TargetLanguage == "pl");
    }

    [Fact]
    public async Task ProcessMedia_WithAutoModeAndNoConfiguredSourceLanguages_QueuesExternalTranslation()
    {
        var movie = await CreateTestMovie("auto.external.mkv");
        var subtitles = new List<Subtitles>
        {
            new()
            {
                Path = "/movies/test/auto.external.en.srt",
                FileName = "auto.external.en",
                Language = "en",
                Caption = "",
                Format = ".srt"
            }
        };

        SubtitleServiceMock
            .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(subtitles);

        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync([]);
        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync([new TargetLanguage { Code = "ro", Name = "Romanian" }]);
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("true");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SourceLanguageMode))
            .ReturnsAsync("auto");

        SourceSubtitleSnapshotServiceMock
            .Setup(service => service.ResolveExternalSourceWithAutoAsync(
                It.IsAny<Lingarr.Core.Interfaces.IMedia>(),
                It.IsAny<IReadOnlyCollection<Subtitles>>(),
                true,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new ResolvedExternalSourceSubtitle
            {
                Subtitle = subtitles[0],
                SourceLanguage = "en",
                Snapshot = new SourceSubtitleSnapshot
                {
                    SourceType = SourceSubtitleSnapshot.ExternalType,
                    SourceLanguage = "en",
                    SourcePath = subtitles[0].Path,
                    Identity = "external|en|/movies/test/auto.external.en.srt",
                    Fingerprint = "fp:auto.external.en.srt"
                }
            });

        var result = await Processor.ProcessMedia(movie, MediaType.Movie);

        Assert.True(result);
        TranslationRequestServiceMock.Verify(
            service => service.CreateRequest(
                It.Is<TranslateAbleSubtitle>(request =>
                    request.SourceLanguage == "en" &&
                    request.TargetLanguage == "ro" &&
                    request.SubtitlePath == subtitles[0].Path),
                It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMediaForceAsync_WithAutoModeAndNoConfiguredSourceLanguages_QueuesEmbeddedTranslation()
    {
        var movie = await CreateTestMovie("auto.embedded.mkv");
        var embeddedSubtitle = new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 1,
            Language = "eng",
            Title = "English Full",
            CodecName = "subrip",
            IsTextBased = true,
            IsForced = false
        };

        movie.EmbeddedSubtitles.Add(embeddedSubtitle);
        await DbContext.EmbeddedSubtitles.AddAsync(embeddedSubtitle);
        await DbContext.SaveChangesAsync();

        SubtitleServiceMock
            .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>());
        SubtitleExtractionServiceMock
            .Setup(service => service.SyncEmbeddedSubtitles(It.IsAny<Movie>()))
            .Returns(Task.CompletedTask);

        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync([]);
        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync([new TargetLanguage { Code = "ro", Name = "Romanian" }]);
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("true");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SourceLanguageMode))
            .ReturnsAsync("auto");

        var queued = await Processor.ProcessMediaForceAsync(movie, MediaType.Movie, forceProcess: true);

        Assert.Equal(1, queued);
        TranslationRequestServiceMock.Verify(
            service => service.CreateRequest(
                It.Is<TranslateAbleSubtitle>(request =>
                    request.MediaId == movie.Id &&
                    request.MediaType == MediaType.Movie &&
                    request.SubtitlePath == null &&
                    request.SourceLanguage == "en" &&
                    request.TargetLanguage == "ro"),
                It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMediaForceAsync_WithAutoModeAndNoConfiguredSourceLanguages_QueuesOcr()
    {
        var movie = await CreateTestMovie("auto.ocr.mkv");
        var embeddedSubtitle = new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 2,
            Language = "eng",
            Title = "English PGS",
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.NotStarted
        };

        movie.EmbeddedSubtitles.Add(embeddedSubtitle);
        await DbContext.EmbeddedSubtitles.AddAsync(embeddedSubtitle);
        await DbContext.SaveChangesAsync();

        SubtitleServiceMock
            .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>());
        SubtitleExtractionServiceMock
            .Setup(service => service.SyncEmbeddedSubtitles(It.IsAny<Movie>()))
            .Returns(Task.CompletedTask);
        SubtitleOcrServiceMock
            .Setup(service => service.IsSupportedCodec("hdmv_pgs_subtitle"))
            .Returns(true);
        SubtitleOcrServiceMock
            .Setup(service => service.QueueOcrAsync(
                movie.Id,
                MediaType.Movie,
                embeddedSubtitle.StreamIndex,
                false,
                It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new SubtitleOcrResult
            {
                Success = true,
                Status = SubtitleOcrStatus.Queued
            });

        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync([]);
        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync([new TargetLanguage { Code = "ro", Name = "Romanian" }]);
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("true");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SourceLanguageMode))
            .ReturnsAsync("auto");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrEnabled))
            .ReturnsAsync("true");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrAutoQueue))
            .ReturnsAsync("true");

        var queued = await Processor.ProcessMediaForceAsync(movie, MediaType.Movie, forceProcess: true);

        Assert.Equal(0, queued);
        SubtitleOcrServiceMock.Verify(
            service => service.QueueOcrAsync(
                movie.Id,
                MediaType.Movie,
                embeddedSubtitle.StreamIndex,
                false,
                It.IsAny<System.Threading.CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMediaForceAsync_WithStaleProcessingOcr_QueuesOcrAgain()
    {
        var movie = await CreateTestMovie("stale.ocr.mkv");
        var embeddedSubtitle = new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 2,
            Language = "eng",
            Title = "English PGS",
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.Processing,
            OcrAttemptedAt = DateTime.UtcNow.AddDays(-1)
        };

        movie.EmbeddedSubtitles.Add(embeddedSubtitle);
        await DbContext.EmbeddedSubtitles.AddAsync(embeddedSubtitle);
        await DbContext.SaveChangesAsync();

        SubtitleServiceMock
            .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>());
        SubtitleExtractionServiceMock
            .Setup(service => service.SyncEmbeddedSubtitles(It.IsAny<Movie>()))
            .Returns(Task.CompletedTask);
        SubtitleOcrServiceMock
            .Setup(service => service.IsSupportedCodec("hdmv_pgs_subtitle"))
            .Returns(true);
        SubtitleOcrServiceMock
            .Setup(service => service.QueueOcrAsync(
                movie.Id,
                MediaType.Movie,
                embeddedSubtitle.StreamIndex,
                false,
                It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new SubtitleOcrResult
            {
                Success = true,
                Status = SubtitleOcrStatus.Queued
            });

        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync([]);
        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync([new TargetLanguage { Code = "ro", Name = "Romanian" }]);
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("true");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SourceLanguageMode))
            .ReturnsAsync("auto");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrEnabled))
            .ReturnsAsync("true");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrAutoQueue))
            .ReturnsAsync("true");

        var queued = await Processor.ProcessMediaForceAsync(movie, MediaType.Movie, forceProcess: true);

        Assert.Equal(0, queued);
        SubtitleOcrServiceMock.Verify(
            service => service.QueueOcrAsync(
                movie.Id,
                MediaType.Movie,
                embeddedSubtitle.StreamIndex,
                false,
                It.IsAny<System.Threading.CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMediaForceAsync_WithFreshProcessingOcr_DoesNotQueueDuplicateOcr()
    {
        var movie = await CreateTestMovie("fresh.ocr.mkv");
        var embeddedSubtitle = new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 2,
            Language = "eng",
            Title = "English PGS",
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.Processing,
            OcrAttemptedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        movie.EmbeddedSubtitles.Add(embeddedSubtitle);
        await DbContext.EmbeddedSubtitles.AddAsync(embeddedSubtitle);
        await DbContext.SaveChangesAsync();

        SubtitleServiceMock
            .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>());
        SubtitleExtractionServiceMock
            .Setup(service => service.SyncEmbeddedSubtitles(It.IsAny<Movie>()))
            .Returns(Task.CompletedTask);
        SubtitleOcrServiceMock
            .Setup(service => service.IsSupportedCodec("hdmv_pgs_subtitle"))
            .Returns(true);

        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync([]);
        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync([new TargetLanguage { Code = "ro", Name = "Romanian" }]);
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("true");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SourceLanguageMode))
            .ReturnsAsync("auto");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrEnabled))
            .ReturnsAsync("true");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrAutoQueue))
            .ReturnsAsync("true");

        var queued = await Processor.ProcessMediaForceAsync(movie, MediaType.Movie, forceProcess: true);

        Assert.Equal(0, queued);
        SubtitleOcrServiceMock.Verify(
            service => service.QueueOcrAsync(
                It.IsAny<int>(),
                It.IsAny<MediaType>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<System.Threading.CancellationToken>()),
            Times.Never);
    }
}
