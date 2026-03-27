namespace Lingarr.Server.Interfaces.Services.Translation;

public interface ITokenUsageService
{
    /// <summary>
    /// Checks if tokens are available for the given service.
    /// If limit is reached, waits until reset time or cancellation.
    /// </summary>
    /// <param name="service">Service name (e.g., "openai", "anthropic")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="OperationCanceledException">If cancelled while waiting</exception>
    Task EnsureTokensAvailableAsync(string service, CancellationToken cancellationToken);
    
    /// <summary>
    /// Records token usage after a successful API call.
    /// Updates cached usage for faster limit checks.
    /// </summary>
    /// <param name="service">Service name</param>
    /// <param name="promptTokens">Input tokens (for logging)</param>
    /// <param name="completionTokens">Output tokens (for limit tracking)</param>
    Task RecordUsageAsync(string service, int? promptTokens, int? completionTokens);
    
    /// <summary>
    /// Gets current token usage for a service.
    /// </summary>
    /// <param name="service">Service name</param>
    /// <returns>Usage snapshot with used/limit/reset time</returns>
    Task<TokenUsageSnapshot> GetUsageAsync(string service);
}

public class TokenUsageSnapshot
{
    public string Service { get; set; } = string.Empty;
    public long TokensUsedToday { get; set; }
    public long? TokenLimit { get; set; }
    public DateTime? ResetAt { get; set; }
    public DateTime LastUpdated { get; set; }
    public bool LimitEnabled => TokenLimit.HasValue && TokenLimit > 0;
    public double PercentUsed => TokenLimit > 0 ? (double)TokensUsedToday / TokenLimit.Value * 100 : 0;
}
