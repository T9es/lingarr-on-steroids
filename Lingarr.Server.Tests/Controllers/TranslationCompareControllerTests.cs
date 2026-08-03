using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Controllers;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Models.Translation;
using Lingarr.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lingarr.Server.Tests.Controllers;

public class TranslationCompareControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LingarrDbContext _dbContext;
    private readonly string _tempDirectory;

    public TranslationCompareControllerTests()
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
    public async Task GetCompletedTranslationCompare_ReturnsCompareLinesForCompletedRequest()
    {
        var sourcePath = Path.Combine(_tempDirectory, "source.en.srt");
        var translatedPath = Path.Combine(_tempDirectory, "source.pl.srt");

        await File.WriteAllTextAsync(
            sourcePath,
            "1\n00:00:01,000 --> 00:00:02,000\nHello there\n\n2\n00:00:03,000 --> 00:00:04,000\nHow are you?\n");

        await File.WriteAllTextAsync(
            translatedPath,
            "1\n00:00:00,000 --> 00:00:00,800\n# Translated with Lingarr using OpenAI translator #\n\n" +
            "2\n00:00:01,000 --> 00:00:02,000\nCzesc tam\n\n" +
            "3\n00:00:03,000 --> 00:00:04,000\nJak sie masz?\n");

        var request = new TranslationRequest
        {
            Title = "Sample Movie",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            TranslatedSubtitle = translatedPath,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Completed,
            CompletedAt = DateTime.UtcNow
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var controller = CreateController();

        var actionResult = await controller.GetCompletedTranslationCompare(request.Id);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var payload = Assert.IsType<CompletedTranslationCompareResponse>(okResult.Value);

        Assert.Equal(request.Id, payload.TranslationRequestId);
        Assert.Equal("Sample Movie", payload.Title);
        Assert.Equal(2, payload.OriginalLineCount);
        Assert.Equal(2, payload.TranslatedLineCount);
        Assert.Equal(2, payload.Lines.Count);

        Assert.Equal("Hello there", payload.Lines[0].Original);
        Assert.Equal("Czesc tam", payload.Lines[0].Translated);
        Assert.Equal(1000, payload.Lines[0].StartTimeMs);
        Assert.Equal(1000, payload.Lines[0].DurationMs);
    }

    [Fact]
    public async Task GetCompletedTranslationCompare_WhenTranslatedCueIsMissing_AlignsBySubtitlePosition()
    {
        var sourcePath = Path.Combine(_tempDirectory, "missing-cue-source.en.srt");
        var translatedPath = Path.Combine(_tempDirectory, "missing-cue-source.pl.srt");

        await File.WriteAllTextAsync(
            sourcePath,
            "1\n00:00:01,000 --> 00:00:02,000\nHello\n\n" +
            "2\n00:00:03,000 --> 00:00:04,000\n-\n-\n\n" +
            "3\n00:00:05,000 --> 00:00:06,000\nGoodbye\n");

        await File.WriteAllTextAsync(
            translatedPath,
            "1\n00:00:01,000 --> 00:00:02,000\nCzesc\n\n" +
            "3\n00:00:05,000 --> 00:00:06,000\nDo widzenia\n");

        var request = new TranslationRequest
        {
            Title = "Missing Cue Movie",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            TranslatedSubtitle = translatedPath,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Completed,
            CompletedAt = DateTime.UtcNow
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var controller = CreateController();

        var actionResult = await controller.GetCompletedTranslationCompare(request.Id);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var payload = Assert.IsType<CompletedTranslationCompareResponse>(okResult.Value);

        Assert.Equal(3, payload.Lines.Count);
        Assert.Equal(1, payload.Lines[0].Position);
        Assert.Equal("Czesc", payload.Lines[0].Translated);
        Assert.Equal(2, payload.Lines[1].Position);
        Assert.Equal("- -", payload.Lines[1].Original);
        Assert.Null(payload.Lines[1].Translated);
        Assert.False(payload.Lines[1].Success);
        Assert.Equal(3, payload.Lines[2].Position);
        Assert.Equal("Do widzenia", payload.Lines[2].Translated);
    }

    [Fact]
    public async Task GetCompletedTranslationCompare_ReturnsBadRequestWhenRequestIsNotCompleted()
    {
        var sourcePath = Path.Combine(_tempDirectory, "pending.en.srt");
        await File.WriteAllTextAsync(sourcePath, "1\n00:00:01,000 --> 00:00:02,000\nPending\n");

        var request = new TranslationRequest
        {
            Title = "Pending Movie",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            TranslatedSubtitle = sourcePath,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var controller = CreateController();

        var actionResult = await controller.GetCompletedTranslationCompare(request.Id);
        Assert.IsType<BadRequestObjectResult>(actionResult.Result);
    }

    [Fact]
    public async Task GetCompletedTranslationCompare_ReturnsNotFoundWhenTranslatedPathIsMissing()
    {
        var sourcePath = Path.Combine(_tempDirectory, "missing-translated.en.srt");
        await File.WriteAllTextAsync(sourcePath, "1\n00:00:01,000 --> 00:00:02,000\nHello\n");

        var request = new TranslationRequest
        {
            Title = "Missing Path",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            TranslatedSubtitle = null,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Completed,
            CompletedAt = DateTime.UtcNow
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var controller = CreateController();

        var actionResult = await controller.GetCompletedTranslationCompare(request.Id);
        Assert.IsType<NotFoundObjectResult>(actionResult.Result);
    }

    [Fact]
    public async Task GetCompletedTranslationCompare_ResolvesTranslatedPathWhenStoredPathIsMissing()
    {
        var sourcePath = Path.Combine(_tempDirectory, "legacy.en.srt");
        var translatedPath = Path.Combine(_tempDirectory, "legacy.pl.srt");

        await File.WriteAllTextAsync(
            sourcePath,
            "1\n00:00:01,000 --> 00:00:02,000\nHello there\n");
        await File.WriteAllTextAsync(
            translatedPath,
            "1\n00:00:01,000 --> 00:00:02,000\nCzesc tam\n");

        var request = new TranslationRequest
        {
            Title = "Legacy Request",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            TranslatedSubtitle = null,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Completed,
            CompletedAt = DateTime.UtcNow
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var controller = CreateController();

        var actionResult = await controller.GetCompletedTranslationCompare(request.Id);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var payload = Assert.IsType<CompletedTranslationCompareResponse>(okResult.Value);

        Assert.Equal(translatedPath, payload.TranslatedSubtitlePath);

        var persistedRequest = await _dbContext.TranslationRequests.FindAsync(request.Id);
        Assert.NotNull(persistedRequest);
        Assert.Equal(translatedPath, persistedRequest!.TranslatedSubtitle);
    }

    [Fact]
    public async Task GetCompletedTranslationCompare_DoesNotCompareSourcePathAsTranslatedOutput()
    {
        var sourcePath = Path.Combine(_tempDirectory, "source.jpn.ass");

        await File.WriteAllTextAsync(
            sourcePath,
            "1\n00:00:01,000 --> 00:00:02,000\nJapanese source\n");

        var request = new TranslationRequest
        {
            Title = "Bad Legacy Request",
            SourceLanguage = "ja",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            TranslatedSubtitle = sourcePath,
            MediaType = MediaType.Episode,
            Status = TranslationStatus.Completed,
            CompletedAt = DateTime.UtcNow
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var controller = CreateController();

        var actionResult = await controller.GetCompletedTranslationCompare(request.Id);

        Assert.IsType<NotFoundObjectResult>(actionResult.Result);
    }

    [Fact]
    public async Task GetCompletedTranslationCompare_UsesGeneratedSubtitlePathWhenPrimaryPathIsSource()
    {
        var sourcePath = Path.Combine(_tempDirectory, "generated-source.jpn.srt");
        var generatedPath = Path.Combine(_tempDirectory, "generated-source.pl.srt");

        await File.WriteAllTextAsync(
            sourcePath,
            "1\n00:00:01,000 --> 00:00:02,000\nJapanese source\n");
        await File.WriteAllTextAsync(
            generatedPath,
            "1\n00:00:01,000 --> 00:00:02,000\nPolski tekst\n");

        var request = new TranslationRequest
        {
            Title = "Recoverable Legacy Request",
            SourceLanguage = "ja",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            TranslatedSubtitle = sourcePath,
            GeneratedSubtitlePaths = JsonSerializer.Serialize(new[] { generatedPath }),
            MediaType = MediaType.Episode,
            Status = TranslationStatus.Completed,
            CompletedAt = DateTime.UtcNow
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var controller = CreateController();

        var actionResult = await controller.GetCompletedTranslationCompare(request.Id);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var payload = Assert.IsType<CompletedTranslationCompareResponse>(okResult.Value);

        Assert.Equal(generatedPath, payload.TranslatedSubtitlePath);
        Assert.Equal("Polski tekst", payload.Lines[0].Translated);

        var persistedRequest = await _dbContext.TranslationRequests.FindAsync(request.Id);
        Assert.NotNull(persistedRequest);
        Assert.Equal(generatedPath, persistedRequest!.TranslatedSubtitle);
    }

    [Fact]
    public async Task GetCompletedTranslationCompare_WhenAssPrimaryHasGeneratedSrt_PrefersSrtForCompare()
    {
        var sourcePath = Path.Combine(_tempDirectory, "ass-source.en.ass");
        var primaryAssPath = Path.Combine(_tempDirectory, "ass-source.pl.ass");
        var generatedSrtPath = Path.Combine(_tempDirectory, "ass-source.pl.srt");

        await File.WriteAllTextAsync(sourcePath, CreateAssSubtitle("Hello from source"));
        await File.WriteAllTextAsync(primaryAssPath, CreateAssSubtitle(@"{\pos(200,130)\bord2\fs75}Unison"));
        await File.WriteAllTextAsync(
            generatedSrtPath,
            "1\n00:00:01,000 --> 00:00:02,000\nClean translated text\n");

        var request = new TranslationRequest
        {
            Title = "ASS Primary Request",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            TranslatedSubtitle = primaryAssPath,
            SourceSubtitleFormat = ".ass",
            SubtitleOutputMode = "both",
            RequiredOutputFormats = ".ass,.srt",
            GeneratedOutputFormats = ".ass,.srt",
            GeneratedSubtitlePaths = JsonSerializer.Serialize(new[] { primaryAssPath, generatedSrtPath }),
            MediaType = MediaType.Episode,
            Status = TranslationStatus.Completed,
            CompletedAt = DateTime.UtcNow
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var controller = CreateController();

        var actionResult = await controller.GetCompletedTranslationCompare(request.Id);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var payload = Assert.IsType<CompletedTranslationCompareResponse>(okResult.Value);

        Assert.Equal(generatedSrtPath, payload.TranslatedSubtitlePath);
        Assert.Equal("Clean translated text", payload.Lines[0].Translated);

        var persistedRequest = await _dbContext.TranslationRequests.FindAsync(request.Id);
        Assert.NotNull(persistedRequest);
        Assert.Equal(primaryAssPath, persistedRequest!.TranslatedSubtitle);
    }

    [Fact]
    public async Task GetCompletedTranslationCompare_EmbeddedMarkerUsesLingarrTrackBeforeBuiltinTargetTrack()
    {
        var sourcePath = Path.Combine(_tempDirectory, "embedded-source.jpn.srt");
        var mediaPath = Path.Combine(_tempDirectory, "movie.mkv");

        await File.WriteAllTextAsync(
            sourcePath,
            "1\n00:00:01,000 --> 00:00:02,000\nJapanese source\n");
        await File.WriteAllTextAsync(mediaPath, string.Empty);

        var request = new TranslationRequest
        {
            Title = "Embedded Recoverable Request",
            SourceLanguage = "ja",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            TranslatedSubtitle = $"mkv-embedded:stream0|{mediaPath}",
            MediaType = MediaType.Episode,
            Status = TranslationStatus.Completed,
            CompletedAt = DateTime.UtcNow
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var extractionService = new FakeSubtitleExtractionService(
            _tempDirectory,
            [
                new EmbeddedSubtitle
                {
                    StreamIndex = 10,
                    Language = "pol",
                    Title = "Polish [Full]",
                    CodecName = "subrip",
                    IsTextBased = true
                },
                new EmbeddedSubtitle
                {
                    StreamIndex = 11,
                    Language = null,
                    Title = "pl (Lingarr)",
                    CodecName = "subrip",
                    IsTextBased = true
                }
            ]);
        var controller = CreateController(extractionService: extractionService);

        var actionResult = await controller.GetCompletedTranslationCompare(request.Id);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var payload = Assert.IsType<CompletedTranslationCompareResponse>(okResult.Value);

        Assert.Equal(11, extractionService.LastExtractedStreamIndex);
        Assert.Equal("Extracted stream 11", payload.Lines[0].Translated);
    }

    [Fact]
    public async Task GetCompletedTranslationCompare_ResolvesMissingSourceSubtitleThroughSharedResolver()
    {
        var missingSourcePath = Path.Combine(_tempDirectory, "missing-source.en.srt");
        var translatedPath = Path.Combine(_tempDirectory, "recovered.pl.srt");
        var recoveredSourcePath = Path.Combine(_tempDirectory, "resolver-source.en.srt");

        await File.WriteAllTextAsync(
            translatedPath,
            "1\n00:00:01,000 --> 00:00:02,000\nOdzyskany tekst\n");
        await File.WriteAllTextAsync(
            recoveredSourcePath,
            "1\n00:00:01,000 --> 00:00:02,000\nRecovered source line\n");

        var request = new TranslationRequest
        {
            Title = "Recovered Movie",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = missingSourcePath,
            TranslatedSubtitle = translatedPath,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Completed,
            CompletedAt = DateTime.UtcNow
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var resolver = new FakeSourceSubtitleResolver(recoveredSourcePath);
        var controller = CreateController(sourceSubtitleResolver: resolver);

        var actionResult = await controller.GetCompletedTranslationCompare(request.Id);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var payload = Assert.IsType<CompletedTranslationCompareResponse>(okResult.Value);

        Assert.Equal("Recovered source line", payload.Lines[0].Original);
        Assert.Equal("Odzyskany tekst", payload.Lines[0].Translated);
        Assert.Equal(recoveredSourcePath, payload.OriginalSubtitlePath);
        Assert.Equal(1, resolver.ResolveCalls);
    }

    [Fact]
    public async Task AcceptTranslation_WhenFailedCueRowsExist_PreservesOnlyUnapprovedCueRows()
    {
        var sourcePath = Path.Combine(_tempDirectory, "failed-source.en.srt");
        await File.WriteAllTextAsync(
            sourcePath,
            "1\n00:00:01,000 --> 00:00:02,000\nLine one\n\n" +
            "2\n00:00:03,000 --> 00:00:04,000\nLine two\n\n" +
            "3\n00:00:05,000 --> 00:00:06,000\nLine three\n");

        var request = new TranslationRequest
        {
            Title = "Failed Movie",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Failed
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        _dbContext.TranslationRequestLogs.Add(new TranslationRequestLog
        {
            TranslationRequestId = request.Id,
            Level = "Error",
            Message = "legacy missing output",
            Details = "Translation failed: 3 subtitle(s) missing at positions: 1, 2, 3."
        });
        _dbContext.TranslationFailedCues.AddRange(
            new TranslationFailedCue
            {
                TranslationRequestId = request.Id,
                Position = 2,
                SourceText = "Line two",
                NormalizedText = "line two",
                TextHash = "hash-two",
                AutoApprovalEligible = true
            },
            new TranslationFailedCue
            {
                TranslationRequestId = request.Id,
                Position = 3,
                SourceText = "Line three",
                NormalizedText = "line three",
                TextHash = "hash-three",
                AutoApprovalEligible = true,
                AutoApprovedAt = DateTime.UtcNow
            });
        await _dbContext.SaveChangesAsync();

        var completionService = new RecordingFailedTranslationCompletionService();
        var controller = CreateController(
            checkpointService: new StaticTranslationCheckpointService(new TranslationCheckpoint
            {
                TranslationRequestId = request.Id,
                SourceFingerprint = "fingerprint",
                Translations = { [1] = "Linia jeden" }
            }),
            completionService: completionService);

        await controller.AcceptTranslation(
            request.Id,
            new TranslationCompareEditRequest(),
            CancellationToken.None);

        Assert.Equal([2], completionService.SourceTextPositions.Order());
    }

    [Fact]
    public async Task SaveTranslation_WhenAssRequestResolvesToSrtOutput_SanitizesEditedSrt()
    {
        var sourcePath = Path.Combine(_tempDirectory, "edited-source.en.ass");
        var assPath = Path.Combine(_tempDirectory, "edited-source.pl.ass");
        var srtPath = Path.Combine(_tempDirectory, "edited-source.pl.srt");

        await File.WriteAllTextAsync(sourcePath, CreateAssSubtitle("Hello"));
        await File.WriteAllTextAsync(assPath, CreateAssSubtitle("Existing ASS"));
        await File.WriteAllTextAsync(
            srtPath,
            "1\n00:00:01,000 --> 00:00:02,000\nExisting SRT\n");

        var request = new TranslationRequest
        {
            Title = "Edited ASS and SRT Request",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            TranslatedSubtitle = assPath,
            SourceSubtitleFormat = ".ass",
            SubtitleOutputMode = "both",
            RequiredOutputFormats = ".ass,.srt",
            GeneratedOutputFormats = ".ass,.srt",
            GeneratedSubtitlePaths = JsonSerializer.Serialize(new[] { assPath, srtPath }),
            MediaType = MediaType.Episode,
            Status = TranslationStatus.Completed,
            CompletedAt = DateTime.UtcNow
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var controller = CreateController();
        var actionResult = await controller.SaveTranslation(
            request.Id,
            new TranslationCompareEditRequest
            {
                Edits =
                [
                    new TranslationCompareEdit
                    {
                        Position = 1,
                        TranslatedText = @"{\pos(200,130)}Czesc"
                    }
                ]
            },
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(actionResult.Result);
        var savedSrt = await File.ReadAllTextAsync(srtPath);
        Assert.Contains("Czesc", savedSrt, StringComparison.Ordinal);
        Assert.DoesNotContain("{\\", savedSrt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveTranslation_WhenAssRequestResolvesToAssOutput_PreservesEditedAssTags()
    {
        var sourcePath = Path.Combine(_tempDirectory, "edited-ass-source.en.ass");
        var assPath = Path.Combine(_tempDirectory, "edited-ass-source.pl.ass");

        await File.WriteAllTextAsync(sourcePath, CreateAssSubtitle("Hello"));
        await File.WriteAllTextAsync(assPath, CreateAssSubtitle("Existing ASS"));

        var request = new TranslationRequest
        {
            Title = "Edited ASS Request",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            TranslatedSubtitle = assPath,
            SourceSubtitleFormat = ".ass",
            SubtitleOutputMode = "ass-only",
            RequiredOutputFormats = ".ass",
            GeneratedOutputFormats = ".ass",
            GeneratedSubtitlePaths = JsonSerializer.Serialize(new[] { assPath }),
            MediaType = MediaType.Episode,
            Status = TranslationStatus.Completed,
            CompletedAt = DateTime.UtcNow
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var controller = CreateController();
        var actionResult = await controller.SaveTranslation(
            request.Id,
            new TranslationCompareEditRequest
            {
                Edits =
                [
                    new TranslationCompareEdit
                    {
                        Position = 1,
                        TranslatedText = @"{\pos(200,130)}Czesc"
                    }
                ]
            },
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(actionResult.Result);
        var savedAss = await File.ReadAllTextAsync(assPath);
        Assert.Contains(@"{\pos(200,130)}Czesc", savedAss, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCompletedTranslationCompare_WhenSrtDropsAssDrawingCues_MatchesByTimestamp()
    {
        var sourcePath = Path.Combine(_tempDirectory, "sparse-ass-source.en.ass");
        var translatedPath = Path.Combine(_tempDirectory, "sparse-ass-source.pl.srt");
        var drawingCue = @"{\p1}m 0 0 l 10 10{\p0}";

        await File.WriteAllTextAsync(
            sourcePath,
            CreateAssSubtitleWithCues(drawingCue, "Hello", drawingCue, "Goodbye"));
        await File.WriteAllTextAsync(
            translatedPath,
            "1\n00:00:02,000 --> 00:00:03,000\nCzesc\n\n" +
            "2\n00:00:04,000 --> 00:00:05,000\nDo widzenia\n");

        var request = new TranslationRequest
        {
            Title = "Sparse ASS Compare",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            TranslatedSubtitle = translatedPath,
            SourceSubtitleFormat = ".ass",
            MediaType = MediaType.Episode,
            Status = TranslationStatus.Completed,
            CompletedAt = DateTime.UtcNow
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var actionResult = await CreateController().GetCompletedTranslationCompare(request.Id);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var payload = Assert.IsType<CompletedTranslationCompareResponse>(okResult.Value);

        Assert.Equal([2, 4], payload.Lines.Select(line => line.Position));
        Assert.Equal("Czesc", payload.Lines.Single(line => line.Position == 2).Translated);
        Assert.Equal("Do widzenia", payload.Lines.Single(line => line.Position == 4).Translated);
    }

    private TranslationCompareController CreateController(
        ISubtitleExtractionService? extractionService = null,
        ISourceSubtitleResolver? sourceSubtitleResolver = null,
        ITranslationCheckpointService? checkpointService = null,
        IFailedTranslationCompletionService? completionService = null)
    {
        return new TranslationCompareController(
            _dbContext,
            new FakeSettingService(new Dictionary<string, string>
            {
                [SettingKeys.Translation.UseSubtitleTagging] = "false",
                [SettingKeys.Translation.RemoveLanguageTag] = "false",
                [SettingKeys.Translation.SubtitleTag] = "[Lingarr]",
                [SettingKeys.Translation.SubtitleTagShort] = "-ai-"
            }),
            extractionService ?? new FakeSubtitleExtractionService(_tempDirectory),
            sourceSubtitleResolver ?? new FakeSourceSubtitleResolver(null),
            new SubtitleService(NullLogger<SubtitleService>.Instance),
            checkpointService ?? new FakeTranslationCheckpointService(),
            completionService ?? new FakeFailedTranslationCompletionService(),
            NullLogger<TranslationCompareController>.Instance);
    }

    private static string CreateAssSubtitle(string text)
        => CreateAssSubtitleWithCues(text);

    private static string CreateAssSubtitleWithCues(params string[] texts)
    {
        var events = texts.Select((text, index) =>
            $"Dialogue: 0,0:00:{index + 1:00}.00,0:00:{index + 2:00}.00,Default,,0,0,0,,{text}");

        return "[Script Info]\n" +
               "ScriptType: v4.00+\n\n" +
               "[V4+ Styles]\n" +
               "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n" +
               "Style: Default,Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,1,0,2,10,10,10,1\n\n" +
               "[Events]\n" +
               "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
               string.Join("\n", events) + "\n";
    }

    private sealed class FakeSubtitleExtractionService : ISubtitleExtractionService
    {
        private readonly List<EmbeddedSubtitle> _embeddedSubtitles;

        public FakeSubtitleExtractionService(
            string tempDirectory,
            List<EmbeddedSubtitle>? embeddedSubtitles = null)
        {
            _embeddedSubtitles = embeddedSubtitles ?? [];
        }

        public int ExtractCalls { get; private set; }

        public string? LastExtractedPath { get; private set; }

        public int? LastExtractedStreamIndex { get; private set; }

        public Task<List<EmbeddedSubtitle>> ProbeEmbeddedSubtitles(string mediaFilePath)
            => Task.FromResult(_embeddedSubtitles);

        public Task<List<AvailableSubtitleResponse>> ListAvailableSubtitlesAsync(
            int mediaId,
            MediaType mediaType)
            => Task.FromResult(new List<AvailableSubtitleResponse>());

        public async Task<string?> ExtractSubtitle(
            string mediaFilePath,
            int streamIndex,
            string outputDirectory,
            string codecName,
            string? language)
        {
            ExtractCalls++;
            Directory.CreateDirectory(outputDirectory);

            var extractedPath = Path.Combine(
                outputDirectory,
                $"compare-{Guid.NewGuid():N}.{(codecName == "ass" ? "ass" : "srt")}");

            await File.WriteAllTextAsync(
                extractedPath,
                "1\n00:00:01,000 --> 00:00:02,000\nRecovered source line\n");

            LastExtractedPath = extractedPath;
            return extractedPath;
        }

        public async Task<string?> ExtractSubtitleToFile(
            string mediaFilePath,
            int streamIndex,
            string outputPath,
            string codecName)
        {
            ExtractCalls++;
            LastExtractedStreamIndex = streamIndex;
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(
                outputPath,
                $"1\n00:00:01,000 --> 00:00:02,000\nExtracted stream {streamIndex}\n");
            LastExtractedPath = outputPath;
            return outputPath;
        }

        public Task SyncEmbeddedSubtitles(Episode episode) => Task.CompletedTask;

        public Task<string?> TryExtractEmbeddedSubtitle(
            int mediaId,
            MediaType mediaType,
            string sourceLanguage,
            List<int>? excludedStreamIndices = null,
            int? preferredStreamIndex = null)
            => Task.FromResult<string?>(null);

        public Task<string?> TryExtractEmbeddedSubtitleForRequestAsync(
            int mediaId,
            MediaType mediaType,
            string sourceLanguage,
            List<int>? excludedStreamIndices = null,
            int? preferredStreamIndex = null)
            => Task.FromResult<string?>(null);

        public Task ClearExtractionMetadataAsync(int mediaId, MediaType mediaType, string extractedPath)
            => Task.CompletedTask;

        public Task SyncEmbeddedSubtitles(Movie movie) => Task.CompletedTask;

        public Task<bool> IsFfmpegAvailable() => Task.FromResult(true);
    }

    private sealed class FakeSourceSubtitleResolver : ISourceSubtitleResolver
    {
        private readonly string? _resolvedPath;

        public FakeSourceSubtitleResolver(string? resolvedPath)
        {
            _resolvedPath = resolvedPath;
        }

        public int ResolveCalls { get; private set; }

        public Task<string?> ResolveReadableSourcePathAsync(
            TranslationRequest request,
            CancellationToken cancellationToken = default)
        {
            ResolveCalls++;
            return Task.FromResult(
                !string.IsNullOrWhiteSpace(_resolvedPath) ? _resolvedPath : request.SubtitleToTranslate);
        }
    }

    private sealed class FakeSettingService : ISettingService
    {
        private readonly Dictionary<string, string> _settings;

        public FakeSettingService(Dictionary<string, string> settings)
        {
            _settings = settings;
        }

        public event SettingChangedHandler? SettingChanged
        {
            add { }
            remove { }
        }

        public Task<string?> GetSetting(string key)
        {
            _settings.TryGetValue(key, out var value);
            return Task.FromResult<string?>(value);
        }

        public Task<Dictionary<string, string>> GetSettings(IEnumerable<string> keys)
        {
            var result = new Dictionary<string, string>();
            foreach (var key in keys)
            {
                if (_settings.TryGetValue(key, out var value))
                {
                    result[key] = value;
                }
            }

            return Task.FromResult(result);
        }

        public Task<bool> SetSetting(string key, string value)
        {
            _settings[key] = value;
            return Task.FromResult(true);
        }

        public Task<bool> SetSettings(Dictionary<string, string> settings)
        {
            foreach (var (key, value) in settings)
            {
                _settings[key] = value;
            }

            return Task.FromResult(true);
        }

        public Task<bool> SetEncryptedSetting(string key, string value) => Task.FromResult(false);

        public Task<string?> GetEncryptedSetting(string key) => Task.FromResult<string?>(null);

        public Task<Dictionary<string, string>> GetEncryptedSettings(IEnumerable<string> keys)
            => Task.FromResult(new Dictionary<string, string>());

        public Task<List<T>> GetSettingAsJson<T>(string key) where T : class
        {
            throw new NotImplementedException();
        }
    }

    private sealed class FakeTranslationCheckpointService : ITranslationCheckpointService
    {
        public Task<TranslationCheckpoint?> LoadAsync(
            int translationRequestId,
            string sourceFingerprint,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<TranslationCheckpoint?>(null);
        }

        public Task<TranslationCheckpoint?> LoadByRequestIdAsync(
            int translationRequestId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<TranslationCheckpoint?>(null);
        }

        public Task SaveCheckpointAsync(
            TranslationCheckpoint checkpoint,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task SaveTranslationAsync(
            int translationRequestId,
            string sourceFingerprint,
            int position,
            string translatedText,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync(int translationRequestId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StaticTranslationCheckpointService : ITranslationCheckpointService
    {
        private readonly TranslationCheckpoint _checkpoint;

        public StaticTranslationCheckpointService(TranslationCheckpoint checkpoint)
        {
            _checkpoint = checkpoint;
        }

        public Task<TranslationCheckpoint?> LoadAsync(
            int translationRequestId,
            string sourceFingerprint,
            CancellationToken cancellationToken)
            => Task.FromResult<TranslationCheckpoint?>(_checkpoint);

        public Task<TranslationCheckpoint?> LoadByRequestIdAsync(
            int translationRequestId,
            CancellationToken cancellationToken)
            => Task.FromResult<TranslationCheckpoint?>(_checkpoint);

        public Task SaveCheckpointAsync(
            TranslationCheckpoint checkpoint,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SaveTranslationAsync(
            int translationRequestId,
            string sourceFingerprint,
            int position,
            string translatedText,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task DeleteAsync(int translationRequestId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class FakeFailedTranslationCompletionService : IFailedTranslationCompletionService
    {
        public Task<FailedTranslationCompletionResult> CompleteAsync(
            TranslationRequest request,
            IReadOnlyDictionary<int, string> edits,
            IReadOnlySet<int> sourceTextPositions,
            string logMessage,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new FailedTranslationCompletionResult(
                Completed: false,
                AlreadyCompleted: false,
                OutputPath: null,
                SkippedReason: "Not configured for this test."));
        }
    }

    private sealed class RecordingFailedTranslationCompletionService : IFailedTranslationCompletionService
    {
        public List<int> SourceTextPositions { get; } = [];

        public Task<FailedTranslationCompletionResult> CompleteAsync(
            TranslationRequest request,
            IReadOnlyDictionary<int, string> edits,
            IReadOnlySet<int> sourceTextPositions,
            string logMessage,
            CancellationToken cancellationToken)
        {
            SourceTextPositions.AddRange(sourceTextPositions);
            return Task.FromResult(new FailedTranslationCompletionResult(
                Completed: false,
                AlreadyCompleted: false,
                OutputPath: null,
                SkippedReason: "Captured for test."));
        }
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
