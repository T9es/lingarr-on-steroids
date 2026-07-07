using Lingarr.Server.Exceptions;

namespace Lingarr.Server.Interfaces.Services.Translation;

/// <summary>
/// Circuit breaker for translation providers.
/// Tracks consecutive failures per provider and enters a cooldown state
/// when too many failures occur, preventing wasted API calls to a down provider.
/// </summary>
public interface IProviderCircuitBreaker
{
    /// <summary>
    /// Checks whether the given provider is currently allowed to make requests.
    /// Throws <see cref="ProviderCircuitOpenException"/> if the provider is in cooldown.
    /// </summary>
    /// <param name="providerName">The service name (e.g. "crofai", "gemini", "deepseek")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task EnsureAllowedAsync(string providerName, CancellationToken cancellationToken);

    /// <summary>
    /// Records a successful call to the provider, resetting the failure counter
    /// and closing the circuit if it was half-open.
    /// </summary>
    /// <param name="providerName">The service name</param>
    void RecordSuccess(string providerName);

    /// <summary>
    /// Records a failed call to the provider, incrementing the failure counter.
    /// If the failure threshold is reached, opens the circuit.
    /// </summary>
    /// <param name="providerName">The service name</param>
    /// <param name="exception">The exception that caused the failure</param>
    void RecordFailure(string providerName, Exception exception);

    /// <summary>
    /// Returns true if the provider is currently in cooldown (circuit open).
    /// </summary>
    /// <param name="providerName">The service name</param>
    bool IsInCooldown(string providerName);

    /// <summary>
    /// Returns the current circuit state info for a provider (for diagnostics/dashboard).
    /// </summary>
    /// <param name="providerName">The service name</param>
    CircuitState GetCircuitState(string providerName);
}

/// <summary>
/// Thrown when a circuit breaker blocks a request because the provider is in cooldown.
/// </summary>
public class ProviderCircuitOpenException : InvalidOperationException
{
    public string ProviderName { get; }
    public TimeSpan CooldownRemaining { get; }
    public int ConsecutiveFailures { get; }

    public ProviderCircuitOpenException(
        string providerName,
        TimeSpan cooldownRemaining,
        int consecutiveFailures)
        : base($"Provider '{providerName}' circuit breaker is open. " +
               $"Cooldown remaining: {cooldownRemaining.TotalSeconds:F0}s. " +
               $"Consecutive failures: {consecutiveFailures}. " +
               $"Requests are blocked to prevent wasted API calls.")
    {
        ProviderName = providerName;
        CooldownRemaining = cooldownRemaining;
        ConsecutiveFailures = consecutiveFailures;
    }
}

/// <summary>
/// Represents the state of a provider's circuit breaker.
/// </summary>
public enum CircuitState
{
    /// <summary>
    /// Circuit is closed — requests are allowed.
    /// </summary>
    Closed,

    /// <summary>
    /// Circuit is open — requests are blocked (cooldown).
    /// </summary>
    Open,

    /// <summary>
    /// Circuit is half-open — one test request is allowed to check recovery.
    /// </summary>
    HalfOpen
}