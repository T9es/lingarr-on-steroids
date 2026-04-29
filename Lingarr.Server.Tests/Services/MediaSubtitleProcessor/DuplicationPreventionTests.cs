using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.MediaSubtitleProcessor;

public class DuplicationPreventionTests : MediaSubtitleProcessorTestBase
{
    [Fact]
    public async Task ProcessMediaForceAsync_WithExistingPendingExternalRequest_DoesNotEnqueueDuplicate()
    {
        var movie = await CreateTestMovie();
        SetupStandardSettings();

        var subtitles = new List<Subtitles>
        {
            new()
            {
                Path = "/movies/test/test.movie.en.srt",
                FileName = "test.movie.en",
                Language = "en",
                Caption = "",
                Format = ".srt"
            }
        };

        SubtitleServiceMock
            .Setup(s => s.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(subtitles);

        DbContext.TranslationRequests.Add(new TranslationRequest
        {
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            WorkloadItemKey = $"library:{MediaType.Movie}:{movie.Id}",
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "ro",
            SubtitleToTranslate = subtitles[0].Path,
            SourceSubtitleFormat = ".srt",
            RequiredOutputFormats = ".srt",
            Status = TranslationStatus.Pending,
            IsActive = true
        });
        await DbContext.SaveChangesAsync();

        var queued = await Processor.ProcessMediaForceAsync(
            movie,
            MediaType.Movie,
            forceProcess: false,
            forceTranslation: false);

        Assert.Equal(0, queued);
        TranslationRequestServiceMock.Verify(s => s.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task ProcessMediaForceAsync_WithLegacyUploadIdCollision_StillQueuesLibraryRequest()
    {
        var movie = await CreateTestMovie();
        SetupStandardSettings();

        var subtitles = new List<Subtitles>
        {
            new()
            {
                Path = "/movies/test/test.movie.en.srt",
                FileName = "test.movie.en",
                Language = "en",
                Caption = "",
                Format = ".srt"
            }
        };

        SubtitleServiceMock
            .Setup(s => s.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(subtitles);

        TranslationRequestServiceMock
            .Setup(s => s.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()))
            .ReturnsAsync(123);

        DbContext.TranslationRequests.Add(new TranslationRequest
        {
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            WorkloadKind = TranslationWorkloadKind.Upload,
            WorkloadItemKey = string.Empty,
            Title = "Upload file",
            SourceLanguage = "en",
            TargetLanguage = "ro",
            SubtitleToTranslate = "/uploads/batch-1/originals/file.en.srt",
            SourceSubtitleFormat = ".srt",
            RequiredOutputFormats = ".srt",
            Status = TranslationStatus.Pending,
            IsActive = true
        });
        await DbContext.SaveChangesAsync();

        var queued = await Processor.ProcessMediaForceAsync(
            movie,
            MediaType.Movie,
            forceTranslation: true,
            forceProcess: true);

        Assert.Equal(1, queued);
        TranslationRequestServiceMock.Verify(s => s.CreateRequest(
            It.Is<TranslateAbleSubtitle>(request =>
                request.WorkloadKind == TranslationWorkloadKind.Library &&
                request.MediaId == movie.Id &&
                request.MediaType == MediaType.Movie &&
                request.SourceLanguage == "en" &&
                request.TargetLanguage == "ro"),
            It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMediaForceAsync_IntegrityValidationUsesFullTargetWhenSparseSupplementalTargetExists()
    {
        var movie = await CreateTestMovie();
        SetupStandardSettings();

        var subtitles = new List<Subtitles>
        {
            new()
            {
                Path = "/movies/test/test.movie.en.srt",
                FileName = "test.movie.en.srt",
                Language = "en",
                Caption = "",
                Format = ".srt"
            },
            new()
            {
                Path = "/movies/test/test.movie.ro.forced.srt",
                FileName = "test.movie.ro.forced.srt",
                Language = "ro",
                Caption = "Forced",
                Format = ".srt"
            },
            new()
            {
                Path = "/movies/test/test.movie.ro.srt",
                FileName = "test.movie.ro.srt",
                Language = "ro",
                Caption = "",
                Format = ".srt"
            }
        };

        SubtitleServiceMock
            .Setup(s => s.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(subtitles);

        var validatedTargets = new List<string>();
        SubtitleIntegrityServiceMock
            .Setup(s => s.ValidateIntegrityAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((_, target) => validatedTargets.Add(target))
            .ReturnsAsync((string _, string target) => target == subtitles[2].Path);

        var queuedRequests = new List<TranslateAbleSubtitle>();
        TranslationRequestServiceMock
            .Setup(s => s.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()))
            .Callback<TranslateAbleSubtitle, bool>((request, _) => queuedRequests.Add(request))
            .ReturnsAsync(123);

        var queued = await Processor.ProcessMediaForceAsync(
            movie,
            MediaType.Movie,
            forceProcess: false,
            forceTranslation: false);

        Assert.True(
            queued == 0,
            $"Expected no queue. Validated targets: {string.Join(", ", validatedTargets)}. Queued: {string.Join(", ", queuedRequests.Select(request => $"{request.SourceLanguage}->{request.TargetLanguage}:{request.SubtitlePath ?? "<embedded>"}"))}");
        TranslationRequestServiceMock.Verify(
            s => s.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()),
            Times.Never);
        SubtitleIntegrityServiceMock.Verify(
            s => s.ValidateIntegrityAsync(subtitles[0].Path, subtitles[2].Path),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMediaForceAsync_WithExistingPendingEmbeddedRequest_DoesNotEnqueueDuplicate()
    {
        var movie = await CreateTestMovie();

        var embeddedSubs = new List<EmbeddedSubtitle>
        {
            new()
            {
                MovieId = movie.Id,
                StreamIndex = 0,
                Language = "eng",
                Title = "Signs & Songs",
                CodecName = "ass",
                IsTextBased = true,
                IsDefault = true,
                IsForced = true
            },
            new()
            {
                MovieId = movie.Id,
                StreamIndex = 1,
                Language = "jpn",
                Title = "Full Subtitles",
                CodecName = "ass",
                IsTextBased = true,
                IsDefault = false,
                IsForced = false
            }
        };

        movie.EmbeddedSubtitles.AddRange(embeddedSubs);
        await DbContext.EmbeddedSubtitles.AddRangeAsync(embeddedSubs);
        await DbContext.SaveChangesAsync();

        SubtitleServiceMock
            .Setup(s => s.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>());

        SettingServiceMock
            .Setup(s => s.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync(new List<SourceLanguage>
            {
                new() { Code = "en", Name = "English" },
                new() { Code = "ja", Name = "Japanese" }
            });

        SettingServiceMock
            .Setup(s => s.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync(new List<TargetLanguage>
            {
                new() { Code = "ro", Name = "Romanian" }
            });

        SubtitleExtractionServiceMock
            .Setup(s => s.SyncEmbeddedSubtitles(It.IsAny<Movie>()))
            .Returns(Task.CompletedTask);

        DbContext.TranslationRequests.Add(new TranslationRequest
        {
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            WorkloadItemKey = $"library:{MediaType.Movie}:{movie.Id}",
            Title = movie.Title,
            SourceLanguage = "ja",
            TargetLanguage = "ro",
            SubtitleToTranslate = null,
            SourceSubtitleFormat = ".ass",
            RequiredOutputFormats = ".ass",
            Status = TranslationStatus.Pending,
            IsActive = true
        });
        await DbContext.SaveChangesAsync();

        var queued = await Processor.ProcessMediaForceAsync(movie, MediaType.Movie);

        Assert.Equal(0, queued);
        TranslationRequestServiceMock.Verify(s => s.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task ProcessMediaForceAsync_WithActiveRequestForDifferentOutputFormat_DoesNotEnqueueExternalTranslation()
    {
        var movie = await CreateTestMovie();
        SetupStandardSettings();

        SettingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync("srt-only");

        var subtitles = new List<Subtitles>
        {
            new()
            {
                Path = "/movies/test/test.movie.en.ass",
                FileName = "test.movie.en",
                Language = "en",
                Caption = "",
                Format = ".ass"
            }
        };

        SubtitleServiceMock
            .Setup(s => s.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(subtitles);

        DbContext.TranslationRequests.Add(new TranslationRequest
        {
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            WorkloadItemKey = $"library:{MediaType.Movie}:{movie.Id}",
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "ro",
            SubtitleToTranslate = subtitles[0].Path,
            SourceSubtitleFormat = ".ass",
            RequiredOutputFormats = ".ass",
            Status = TranslationStatus.Pending,
            IsActive = true
        });
        await DbContext.SaveChangesAsync();

        var queued = await Processor.ProcessMediaForceAsync(movie, MediaType.Movie, forceProcess: true);

        Assert.Equal(0, queued);
        TranslationRequestServiceMock.Verify(
            s => s.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessMediaForceAsync_WithAssSourceAndOnlySrtTarget_QueuesMissingAssOutput()
    {
        var movie = await CreateTestMovie();
        SetupStandardSettings();

        SettingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync("both");

        var subtitles = new List<Subtitles>
        {
            new()
            {
                Path = "/movies/test/test.movie.en.ass",
                FileName = "test.movie.en",
                Language = "en",
                Caption = "",
                Format = ".ass"
            },
            new()
            {
                Path = "/movies/test/test.movie.ro.srt",
                FileName = "test.movie.ro",
                Language = "ro",
                Caption = "",
                Format = ".srt"
            }
        };

        SubtitleServiceMock
            .Setup(s => s.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(subtitles);

        TranslationRequestServiceMock
            .Setup(s => s.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()))
            .ReturnsAsync(123);

        var queued = await Processor.ProcessMediaForceAsync(movie, MediaType.Movie, forceProcess: true);

        Assert.Equal(1, queued);
        TranslationRequestServiceMock.Verify(s => s.CreateRequest(
            It.Is<TranslateAbleSubtitle>(request =>
                request.MediaId == movie.Id &&
                request.MediaType == MediaType.Movie &&
                request.SourceLanguage == "en" &&
                request.TargetLanguage == "ro" &&
                request.SubtitlePath == "/movies/test/test.movie.en.ass" &&
                request.SubtitleFormat == ".ass"),
            It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMediaForceAsync_WithActiveRequestForDifferentOutputFormat_DoesNotEnqueueEmbeddedTranslation()
    {
        var movie = await CreateTestMovie();

        SubtitleServiceMock
            .Setup(s => s.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>());

        SettingServiceMock
            .Setup(s => s.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync(new List<SourceLanguage>
            {
                new() { Code = "ja", Name = "Japanese" }
            });

        SettingServiceMock
            .Setup(s => s.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync(new List<TargetLanguage>
            {
                new() { Code = "ro", Name = "Romanian" }
            });

        SettingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("true");

        SettingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.SubtitleValidation.SkipWhenTargetEmbedded))
            .ReturnsAsync("true");

        SettingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync("srt-only");

        var embeddedSubs = new List<EmbeddedSubtitle>
        {
            new()
            {
                MovieId = movie.Id,
                StreamIndex = 0,
                Language = "jpn",
                Title = "Full Subtitles",
                CodecName = "ass",
                IsTextBased = true,
                IsDefault = true,
                IsForced = false
            }
        };

        movie.EmbeddedSubtitles.AddRange(embeddedSubs);
        await DbContext.EmbeddedSubtitles.AddRangeAsync(embeddedSubs);
        await DbContext.SaveChangesAsync();

        SubtitleExtractionServiceMock
            .Setup(s => s.SyncEmbeddedSubtitles(It.IsAny<Movie>()))
            .Returns(Task.CompletedTask);

        DbContext.TranslationRequests.Add(new TranslationRequest
        {
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            WorkloadItemKey = $"library:{MediaType.Movie}:{movie.Id}",
            Title = movie.Title,
            SourceLanguage = "ja",
            TargetLanguage = "ro",
            SubtitleToTranslate = null,
            SourceSubtitleFormat = ".ass",
            RequiredOutputFormats = ".ass",
            Status = TranslationStatus.Pending,
            IsActive = true
        });
        await DbContext.SaveChangesAsync();

        var queued = await Processor.ProcessMediaForceAsync(movie, MediaType.Movie, forceProcess: true);

        Assert.Equal(0, queued);
        TranslationRequestServiceMock.Verify(
            s => s.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessMediaForceAsync_WithEmbeddedAssTargetOnlyAndBothMode_QueuesMissingSrtOutput()
    {
        var movie = await CreateTestMovie();
        TranslateAbleSubtitle? capturedRequest = null;

        SubtitleServiceMock
            .Setup(s => s.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>());

        SettingServiceMock
            .Setup(s => s.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync(new List<SourceLanguage>
            {
                new() { Code = "ja", Name = "Japanese" }
            });

        SettingServiceMock
            .Setup(s => s.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync(new List<TargetLanguage>
            {
                new() { Code = "ro", Name = "Romanian" }
            });

        SettingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("true");

        SettingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.SubtitleValidation.SkipWhenTargetEmbedded))
            .ReturnsAsync("true");

        SettingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync("both");

        var embeddedSubs = new List<EmbeddedSubtitle>
        {
            new()
            {
                MovieId = movie.Id,
                StreamIndex = 0,
                Language = "jpn",
                Title = "Full Subtitles",
                CodecName = "ass",
                IsTextBased = true,
                IsDefault = true,
                IsForced = false
            },
            new()
            {
                MovieId = movie.Id,
                StreamIndex = 1,
                Language = "ron",
                Title = "Polished Dubtitles",
                CodecName = "ass",
                IsTextBased = true,
                IsDefault = false,
                IsForced = false
            }
        };

        movie.EmbeddedSubtitles.AddRange(embeddedSubs);
        await DbContext.EmbeddedSubtitles.AddRangeAsync(embeddedSubs);
        await DbContext.SaveChangesAsync();

        SubtitleExtractionServiceMock
            .Setup(s => s.SyncEmbeddedSubtitles(It.IsAny<Movie>()))
            .Returns(Task.CompletedTask);

        TranslationRequestServiceMock
            .Setup(s => s.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()))
            .Callback<TranslateAbleSubtitle, bool>((request, _) => capturedRequest = request)
            .ReturnsAsync(456);

        var queued = await Processor.ProcessMediaForceAsync(movie, MediaType.Movie);

        Assert.Equal(1, queued);
        Assert.NotNull(capturedRequest);
        Assert.Equal(movie.Id, capturedRequest!.MediaId);
        Assert.Equal(MediaType.Movie, capturedRequest.MediaType);
        Assert.Equal(".ass", SubtitleOutputModeHelper.NormalizeFormat(capturedRequest.SubtitleFormat));
        Assert.Null(capturedRequest.SubtitlePath);
        TranslationRequestServiceMock.Verify(
            s => s.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()),
            Times.Once);
    }
}
