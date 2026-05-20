using System.Collections.Generic;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Controllers;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.Api;
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

    private TranslationCompareController CreateController(
        ISubtitleExtractionService? extractionService = null,
        ISourceSubtitleResolver? sourceSubtitleResolver = null)
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
            NullLogger<TranslationCompareController>.Instance);
    }

    private sealed class FakeSubtitleExtractionService : ISubtitleExtractionService
    {
        public FakeSubtitleExtractionService(string tempDirectory) { }

        public int ExtractCalls { get; private set; }

        public string? LastExtractedPath { get; private set; }

        public Task<List<EmbeddedSubtitle>> ProbeEmbeddedSubtitles(string mediaFilePath)
            => Task.FromResult(new List<EmbeddedSubtitle>());

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
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(
                outputPath,
                "1\n00:00:01,000 --> 00:00:02,000\nRecovered source line\n");
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

        public event SettingChangedHandler? SettingChanged;

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
