namespace Lingarr.Server.Models.Api;

/// <summary>
/// Response model for translate media endpoint.
/// </summary>
public class TranslateMediaResponse
{
    /// <summary>
    /// Number of translations that were queued.
    /// </summary>
    public int TranslationsQueued { get; set; }
    
    /// <summary>
    /// Message describing the result.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
