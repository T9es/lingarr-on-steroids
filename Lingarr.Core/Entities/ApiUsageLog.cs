using System.ComponentModel.DataAnnotations;

namespace Lingarr.Core.Entities;

/// <summary>
/// Tracks API usage for translation services.
/// Stores response time, token counts, and success/failure status.
/// </summary>
public class ApiUsageLog
{
    [Key]
    public int Id { get; set; }
    
    /// <summary>
    /// When the API call was made (UTC)
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// The translation service used (e.g., "openai", "anthropic", "chutes")
    /// </summary>
    [MaxLength(50)]
    public string Service { get; set; } = string.Empty;
    
    /// <summary>
    /// Number of tokens used (null if not provided by service)
    /// </summary>
    public int? TokensUsed { get; set; }
    
    /// <summary>
    /// Number of prompt/input tokens (for pricing calculations)
    /// </summary>
    public int? PromptTokens { get; set; }
    
    /// <summary>
    /// Number of completion/output tokens (for limit tracking)
    /// </summary>
    public int? CompletionTokens { get; set; }
    
    /// <summary>
    /// Response time in milliseconds
    /// </summary>
    public long ResponseTimeMs { get; set; }
    
    /// <summary>
    /// Whether the API call succeeded
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Error message if the call failed
    /// </summary>
    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }
}
