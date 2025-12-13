using Lingarr.Server.Models.Api;

namespace Lingarr.Server.Interfaces.Services;

/// <summary>
/// Service for running test translations with detailed logging without saving results.
/// </summary>
public interface ITestTranslationService
{
    /// <summary>
    /// Event raised when a log entry is generated during test translation.
    /// </summary>
    event EventHandler<TestTranslationLogEntry>? OnLogEntry;
    
    /// <summary>
    /// Starts a test translation for the specified media.
    /// The translation runs with detailed logging but does NOT save the result.
    /// </summary>
    /// <param name="request">Test translation request with media details</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Test translation result with success/failure status</returns>
    Task<TestTranslationResult> RunTestAsync(
        TestTranslationRequest request,
        CancellationToken cancellationToken);
    
    /// <summary>
    /// Cancels any in-progress test translation.
    /// </summary>
    void CancelTest();
    
    /// <summary>
    /// Gets whether a test is currently running.
    /// </summary>
    bool IsRunning { get; }
}

/// <summary>
/// Log entry generated during test translation.
/// </summary>
public class TestTranslationLogEntry
{
    public required string Level { get; set; }
    public required string Message { get; set; }
    public required DateTime Timestamp { get; set; }
    public string? Details { get; set; }
}

/// <summary>
/// Request to start a test translation.
/// </summary>
public class TestTranslationRequest
{
    public string? SubtitlePath { get; set; }
    public int? MediaId { get; set; }
    public Lingarr.Core.Enum.MediaType? MediaType { get; set; }
    public required string SourceLanguage { get; set; }
    public required string TargetLanguage { get; set; }
}

/// <summary>
/// Result of a test translation.
/// </summary>
public class TestTranslationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int TotalSubtitles { get; set; }
    public int TranslatedCount { get; set; }
    public TimeSpan Duration { get; set; }
    public List<TranslatedSubtitlePreview>? Preview { get; set; }
}

/// <summary>
/// Preview of a translated subtitle line.
/// </summary>
public class TranslatedSubtitlePreview
{
    public int Position { get; set; }
    public required string Original { get; set; }
    public required string Translated { get; set; }
}
