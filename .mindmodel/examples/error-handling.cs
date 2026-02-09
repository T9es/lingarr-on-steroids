// Error Handling Pattern Example - TranslationException.cs
// Demonstrates custom exception hierarchy

namespace Lingarr.Server.Exceptions;

public class TranslationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TranslationException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="exception"></param>
    public TranslationException(string message, Exception? exception = null) : base(message, exception)
    {
    }
}

// Specialized exception for translation response errors
public class TranslationResponseException : TranslationException
{
    public string Response { get; }
    
    public TranslationResponseException(string message, string response) 
        : base(message)
    {
        Response = response;
    }
}

// Usage example with structured logging:
/*
_logger.LogInformation(
    "TranslateJob started for subtitle: |Green|{filePath}|/Green|",
    subtitlePathForLog);

_logger.LogWarning(
    "Translation cancelled for subtitle: |Orange|{subtitlePath}|/Orange|",
    request.SubtitleToTranslate);

try
{
    // ... translation logic
}
catch (TaskCanceledException)
{
    await HandleCancellation(request);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Translation failed for request {RequestId}", translationRequest.Id);
    throw;
}
finally
{
    // Cleanup logic
}
*/
