using System.Diagnostics;
using Lingarr.Core.Configuration;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.FileSystem;
using CoreTranslationRequest = Lingarr.Core.Entities.TranslationRequest;

namespace Lingarr.Server.Services;

/// <summary>
/// Service for running test translations with detailed logging without saving results.
/// </summary>
public class TestTranslationService : ITestTranslationService
{
    private readonly ILogger<TestTranslationService> _logger;
    private readonly ISettingService _settings;
    private readonly ISubtitleService _subtitleService;
    private readonly ITranslationServiceFactory _translationServiceFactory;
    private readonly IBatchFallbackService _batchFallbackService;
    private readonly IDeferredRepairService _deferredRepairService;
    private readonly ISubtitleExtractionService _extractionService;
    
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isRunning;
    
    public event EventHandler<TestTranslationLogEntry>? OnLogEntry;
    public bool IsRunning => _isRunning;
    
    public TestTranslationService(
        ILogger<TestTranslationService> logger,
        ISettingService settings,
        ISubtitleService subtitleService,
        ITranslationServiceFactory translationServiceFactory,
        IBatchFallbackService batchFallbackService,
        IDeferredRepairService deferredRepairService,
        ISubtitleExtractionService extractionService)
    {
        _logger = logger;
        _settings = settings;
        _subtitleService = subtitleService;
        _translationServiceFactory = translationServiceFactory;
        _batchFallbackService = batchFallbackService;
        _deferredRepairService = deferredRepairService;
        _extractionService = extractionService;
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
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            string? temporaryFilePath = null;
            try
            {
                var subtitlePath = request.SubtitlePath;
                if (string.IsNullOrEmpty(subtitlePath))
                {
                    if (request.MediaId.HasValue && request.MediaType.HasValue)
                    {
                       Log("INFORMATION", "Subtitle path not provided, attempting embedded subtitle extraction...");
                       subtitlePath = await _extractionService.TryExtractEmbeddedSubtitle(
                           request.MediaId.Value, 
                           request.MediaType.Value, 
                           request.SourceLanguage);
                       
                       if (subtitlePath != null) 
                       {
                           Log("INFORMATION", $"Extracted embedded subtitle to: {subtitlePath}");
                           temporaryFilePath = subtitlePath;
                       }
                       else
                       {
                           throw new InvalidOperationException("Failed to extract embedded subtitle");
                       }
                    }
                    else
                    {
                        throw new ArgumentException("Subtitle path is missing and no media ID/Type provided for extraction");
                    }
                }

                Log("INFORMATION", $"Starting test translation for: {subtitlePath}");
                Log("INFORMATION", $"Source language: {request.SourceLanguage}, Target language: {request.TargetLanguage}");
                
                // Get settings
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
                
                // Read subtitles
                Log("INFORMATION", "Reading subtitle file...");
                var subtitles = await _subtitleService.ReadSubtitles(subtitlePath);
                Log("INFORMATION", $"Read {subtitles.Count} subtitle entries");
                
                // Create translation service
                var translationService = _translationServiceFactory.CreateTranslationService(serviceType);
                var progressService = new TestProgressService(this);
                var translator = new SubtitleTranslationService(
                    translationService, 
                    _logger, 
                    progressService, 
                    _batchFallbackService,
                    _deferredRepairService);
                
                // Build translation request (using test-only values for required db fields)
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
                
                List<SubtitleItem> translated;
                
                if (useBatch && translationService is IBatchTranslationService)
                {
                    var maxSize = int.TryParse(settings[SettingKeys.Translation.MaxBatchSize], out var bs) ? bs : 0;
                    var enableFallback = settings[SettingKeys.Translation.EnableBatchFallback] == "true";
                    var splitAttempts = int.TryParse(settings[SettingKeys.Translation.MaxBatchSplitAttempts], out var sa) ? sa : 3;
                    
                    // For test mode, use immediate fallback (legacy behavior) for simpler debugging
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
                        0, // no context for test
                        0,
                        _cancellationTokenSource.Token);
                }
                
                stopwatch.Stop();
                
                var translatedCount = translated.Count(s => s.TranslatedLines?.Count > 0);
                Log("INFORMATION", $"Translation completed! Translated {translatedCount}/{subtitles.Count} subtitles in {stopwatch.Elapsed.TotalSeconds:F1}s");
                Log("INFORMATION", "NOTE: Translated subtitle was NOT saved (test mode)");
                
                // Create preview of first few translations
                var preview = translated.Take(5).Select(s => new TranslatedSubtitlePreview
                {
                    Position = s.Position,
                    Original = string.Join(" ", s.Lines),
                    Translated = string.Join(" ", s.TranslatedLines ?? s.Lines)
                }).ToList();
                
                return new TestTranslationResult
                {
                    Success = true,
                    TotalSubtitles = subtitles.Count,
                    TranslatedCount = translatedCount,
                    Duration = stopwatch.Elapsed,
                    Preview = preview
                };
            }
            finally
            {
               if (temporaryFilePath != null && File.Exists(temporaryFilePath))
               {
                   try
                   {
                       File.Delete(temporaryFilePath);
                       // Don't log this unless debug
                   }
                   catch { /* ignore cleanup error */ }
               }
            }
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
            _isRunning = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
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
        
        // Also log to standard logger
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
    
    /// <summary>
    /// Internal progress service that logs progress updates to the test log
    /// </summary>
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
            // Only log at 10% intervals to avoid spam
            var rounded = (progress / 10) * 10;
            if (rounded != _lastProgress)
            {
                _lastProgress = rounded;
                _parent.Log("INFORMATION", $"Translation progress: {progress}%");
            }
            return Task.CompletedTask;
        }
    }
}
