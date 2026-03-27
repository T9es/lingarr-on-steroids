using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.FileSystem;
using Microsoft.EntityFrameworkCore;
using CoreTranslationRequest = Lingarr.Core.Entities.TranslationRequest;

namespace Lingarr.Server.Services;

public class TestTranslationService : ITestTranslationService
{
    private static readonly JsonSerializerOptions DebugJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<TestTranslationService> _logger;
    private readonly ISettingService _settings;
    private readonly ISubtitleService _subtitleService;
    private readonly ITranslationServiceFactory _translationServiceFactory;
    private readonly IBatchFallbackService _batchFallbackService;
    private readonly IDeferredRepairService _deferredRepairService;
    private readonly ISubtitleExtractionService _extractionService;
    private readonly LingarrDbContext _dbContext;
    
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isRunning;
    private readonly ITestDebugCollector _debugCollector = new TestDebugCollector();
    
    public event EventHandler<TestTranslationLogEntry>? OnLogEntry;
    public bool IsRunning => _isRunning;
    
    public TestTranslationService(
        ILogger<TestTranslationService> logger,
        ISettingService settings,
        ISubtitleService subtitleService,
        ITranslationServiceFactory translationServiceFactory,
        IBatchFallbackService batchFallbackService,
        IDeferredRepairService deferredRepairService,
        ISubtitleExtractionService extractionService,
        LingarrDbContext dbContext)
    {
        _logger = logger;
        _settings = settings;
        _subtitleService = subtitleService;
        _translationServiceFactory = translationServiceFactory;
        _batchFallbackService = batchFallbackService;
        _deferredRepairService = deferredRepairService;
        _extractionService = extractionService;
        _dbContext = dbContext;
    }
    
    public void CancelTest()
    {
        if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
        {
            Log("WARNING", "Test translation cancellation requested");
            _cancellationTokenSource.Cancel();
        }
    }
    
    public async Task<TestTranslationResult> RunTestAsync(
        TestTranslationRequest request,
        CancellationToken cancellationToken)
    {
        if (_isRunning)
        {
            return new TestTranslationResult
            {
                Success = false,
                ErrorMessage = "A test is already running. Please cancel it first."
            };
        }
        
        _isRunning = true;
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _debugCollector.Reset();
        var stopwatch = Stopwatch.StartNew();
        string? subtitlePath = null;
        string? temporaryFilePath = null;
        string? title = null;
        string? posterPath = null;
        
        try
        {
            subtitlePath = request.SubtitlePath;
            
            if (string.IsNullOrEmpty(subtitlePath) || !File.Exists(subtitlePath))
            {
                if (request.MediaId.HasValue && request.MediaType.HasValue)
                {
                   if (!string.IsNullOrEmpty(subtitlePath))
                   {
                       Log("INFORMATION", $"Subtitle file not found on disk: {subtitlePath}");
                   }
                   Log("INFORMATION", "Attempting embedded subtitle extraction...");
                   subtitlePath = await _extractionService.TryExtractEmbeddedSubtitle(
                       request.MediaId.Value,
                       request.MediaType.Value,
                       request.SourceLanguage,
                       null,
                       request.EmbeddedStreamIndex);

                   if (subtitlePath != null)
                   {
                       Log("INFORMATION", $"Extracted embedded subtitle to: {subtitlePath}");
                       temporaryFilePath = subtitlePath;
                   }
                   else
                   {
                       throw new InvalidOperationException("Failed to extract embedded subtitle - no suitable embedded subtitle found");
                   }
                }
                else
                {
                    throw new ArgumentException("Subtitle path is missing or file not found, and no media ID/Type provided for extraction");
                }
            }

            Log("INFORMATION", $"Starting test translation for: {subtitlePath}");
            Log("INFORMATION", $"Source language: {request.SourceLanguage}, Target language: {request.TargetLanguage}");
            
            var settings = await _settings.GetSettings([
                SettingKeys.Translation.ServiceType,
                SettingKeys.Translation.StripSubtitleFormatting,
                SettingKeys.Translation.UseBatchTranslation,
                SettingKeys.Translation.MaxBatchSize,
                SettingKeys.Translation.EnableBatchFallback,
                SettingKeys.Translation.MaxBatchSplitAttempts
            ]);
            
            var serviceType = settings[SettingKeys.Translation.ServiceType];
            var stripFormatting = settings[SettingKeys.Translation.StripSubtitleFormatting] == "true";
            var useBatch = settings[SettingKeys.Translation.UseBatchTranslation] == "true";
            
            Log("INFORMATION", $"Using translation service: {serviceType}");
            Log("INFORMATION", $"Strip formatting: {stripFormatting}, Batch mode: {useBatch}");
            
            _debugCollector.RecordTiming("Initialization", stopwatch.ElapsedMilliseconds);
            
            Log("INFORMATION", "Reading subtitle file...");
            var readStopwatch = Stopwatch.StartNew();
            var allSubtitles = await _subtitleService.ReadSubtitles(subtitlePath);
            readStopwatch.Stop();
            _debugCollector.RecordTiming("SubtitleReading", readStopwatch.ElapsedMilliseconds);
            Log("INFORMATION", $"Read {allSubtitles.Count} subtitle entries");
            
            var subtitles = FilterSubtitles(allSubtitles, request);
            if (subtitles.Count != allSubtitles.Count)
            {
                Log("INFORMATION", $"Filtered to {subtitles.Count} subtitles (from {allSubtitles.Count}) based on line selection");
            }
            
            var translationService = _translationServiceFactory.CreateTranslationService(serviceType);
            var progressService = new TestProgressService(this);
            var translator = new SubtitleTranslationService(
                translationService, 
                _logger, 
                progressService, 
                _batchFallbackService,
                _deferredRepairService);
            
            var translationRequest = new CoreTranslationRequest
            {
                Title = "Test Translation",
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                SubtitleToTranslate = subtitlePath,
                MediaType = Lingarr.Core.Enum.MediaType.Movie,
                Status = Lingarr.Core.Enum.TranslationStatus.InProgress
            };
            
            _cancellationTokenSource.Token.ThrowIfCancellationRequested();
            
            var translationStopwatch = Stopwatch.StartNew();
            List<SubtitleItem> translated;
            
            if (useBatch && translationService is IBatchTranslationService)
            {
                var maxSize = int.TryParse(settings[SettingKeys.Translation.MaxBatchSize], out var bs) ? bs : 0;
                var enableFallback = settings[SettingKeys.Translation.EnableBatchFallback] == "true";
                var splitAttempts = int.TryParse(settings[SettingKeys.Translation.MaxBatchSplitAttempts], out var sa) ? sa : 3;
                
                var batchRetryMode = enableFallback ? "immediate" : "deferred";
                
                Log("INFORMATION", $"Starting batch translation: batchSize={maxSize}, retryMode={batchRetryMode}, splitAttempts={splitAttempts}");
                
                translated = await translator.TranslateSubtitlesBatch(
                    subtitles,
                    translationRequest,
                    stripFormatting,
                    maxSize,
                    batchRetryMode,
                    splitAttempts,
                    repairContextRadius: 10,
                    repairMaxRetries: 1,
                    batchContextEnabled: false,
                    batchContextBefore: 0,
                    batchContextAfter: 0,
                    fileIdentifier: "Test Translation",
                    cancellationToken: _cancellationTokenSource.Token);
            }
            else
            {
                Log("INFORMATION", "Starting individual line translation...");
                
                translated = await translator.TranslateSubtitles(
                    subtitles,
                    translationRequest,
                    stripFormatting,
                    0,
                    0,
                    _cancellationTokenSource.Token);
            }
            
            translationStopwatch.Stop();
            _debugCollector.RecordTiming("Translation", translationStopwatch.ElapsedMilliseconds);
            
            stopwatch.Stop();
            
            var translatedCount = translated.Count(s => s.TranslatedLines?.Count > 0);
            var failedCount = subtitles.Count - translatedCount;
            Log("INFORMATION", $"Translation completed! Translated {translatedCount}/{subtitles.Count} subtitles in {stopwatch.Elapsed.TotalSeconds:F1}s");
            Log("INFORMATION", "NOTE: Translated subtitle was NOT saved (test mode)");
            
            var preview = translated.Select(s => new TranslatedSubtitlePreview
            {
                Position = s.Position,
                Original = string.Join(" ", s.Lines),
                Translated = string.Join(" ", s.TranslatedLines ?? s.Lines)
            }).ToList();
            
            var lineResults = translated.Select(s => new TestLineResult
            {
                Position = s.Position,
                Original = string.Join(" ", s.Lines),
                Translated = string.Join(" ", s.TranslatedLines ?? []),
                Success = s.TranslatedLines?.Count > 0,
                DurationMs = s.EndTime - s.StartTime,
                StartTimeMs = s.StartTime,
                EndTimeMs = s.EndTime
            }).ToList();
            
            foreach (var line in lineResults)
            {
                _debugCollector.RecordLineResult(line);
            }
            
            var debugData = _debugCollector.GetCollectedData();
            
            var testResult = new TestResult
            {
                SubtitlePath = subtitlePath,
                Title = title,
                PosterPath = posterPath,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                Success = true,
                TotalLines = subtitles.Count,
                TranslatedLines = translatedCount,
                FailedLines = failedCount,
                DurationSeconds = stopwatch.Elapsed.TotalSeconds,
                TranslationService = serviceType,
                ApiCallsJson = JsonSerializer.Serialize(debugData.ApiCalls, DebugJsonOptions),
                LineResultsJson = JsonSerializer.Serialize(debugData.LineResults, DebugJsonOptions),
                TimingJson = JsonSerializer.Serialize(debugData.Timings, DebugJsonOptions),
                PreviewJson = JsonSerializer.Serialize(preview, DebugJsonOptions)
            };
            
            _dbContext.TestResults.Add(testResult);
            await _dbContext.SaveChangesAsync(_cancellationTokenSource.Token);
            
            return new TestTranslationResult
            {
                Success = true,
                TestResultId = testResult.Id,
                TotalSubtitles = subtitles.Count,
                TranslatedCount = translatedCount,
                Duration = stopwatch.Elapsed,
                Preview = preview
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            Log("WARNING", $"Test translation cancelled after {stopwatch.Elapsed.TotalSeconds:F1}s");
            
            return new TestTranslationResult
            {
                Success = false,
                ErrorMessage = "Test translation was cancelled",
                Duration = stopwatch.Elapsed
            };
        }
        catch (TranslationException ex)
        {
            stopwatch.Stop();
            Log("ERROR", $"Translation failed: {ex.Message}");
            
            var testResult = new TestResult
            {
                SubtitlePath = subtitlePath ?? "Unknown",
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                Success = false,
                ErrorMessage = ex.Message,
                DurationSeconds = stopwatch.Elapsed.TotalSeconds,
                TranslationService = "Unknown"
            };
            
            _dbContext.TestResults.Add(testResult);
            await _dbContext.SaveChangesAsync(cancellationToken);
            
            return new TestTranslationResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Log("ERROR", $"Unexpected error: {ex.Message}", ex.StackTrace);
            
            return new TestTranslationResult
            {
                Success = false,
                ErrorMessage = $"Unexpected error: {ex.Message}",
                Duration = stopwatch.Elapsed
            };
        }
        finally
        {
           if (temporaryFilePath != null && File.Exists(temporaryFilePath))
           {
               try
               {
                   File.Delete(temporaryFilePath);
               }
               catch { /* ignore cleanup error */ }
           }
            
            _isRunning = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }
    
    private List<SubtitleItem> FilterSubtitles(List<SubtitleItem> subtitles, TestTranslationRequest request)
    {
        if (request.SelectedLinePositions is { Count: > 0 })
        {
            var selectedPositions = request.SelectedLinePositions.ToHashSet();
            return subtitles.Where(subtitle => selectedPositions.Contains(subtitle.Position)).ToList();
        }

        if (request.MaxLines.HasValue && request.MaxLines.Value > 0)
        {
            return subtitles.Take(request.MaxLines.Value).ToList();
        }
        
        if (request.StartLine.HasValue && request.EndLine.HasValue)
        {
            var start = Math.Max(1, request.StartLine.Value) - 1;
            var end = Math.Min(subtitles.Count, request.EndLine.Value);
            return subtitles.Skip(start).Take(end - start).ToList();
        }
        
        if (request.StartLine.HasValue)
        {
            var start = Math.Max(1, request.StartLine.Value) - 1;
            return subtitles.Skip(start).ToList();
        }
        
        return subtitles;
    }
    
    internal void Log(string level, string message, string? details = null)
    {
        var entry = new TestTranslationLogEntry
        {
            Level = level,
            Message = message,
            Timestamp = DateTime.Now,
            Details = details
        };
        
        OnLogEntry?.Invoke(this, entry);
        
        switch (level.ToUpperInvariant())
        {
            case "ERROR":
                _logger.LogError(message);
                break;
            case "WARNING":
                _logger.LogWarning(message);
                break;
            default:
                _logger.LogInformation(message);
                break;
        }
    }
    
    private class TestProgressService : IProgressService
    {
        private readonly TestTranslationService _parent;
        private int _lastProgress = -1;
        
        public TestProgressService(TestTranslationService parent)
        {
            _parent = parent;
        }
        
        public Task Emit(CoreTranslationRequest request, int progress)
        {
            var rounded = (progress / 10) * 10;
            if (rounded != _lastProgress)
            {
                _lastProgress = rounded;
                _parent.Log("INFORMATION", $"Translation progress: {progress}%");
            }
            return Task.CompletedTask;
        }

        public Task EmitBatch(List<CoreTranslationRequest> requests, int progress)
        {
            _parent.Log("INFORMATION", $"Batch translation progress: {progress}% for {requests.Count} requests");
            return Task.CompletedTask;
        }
    }
}
