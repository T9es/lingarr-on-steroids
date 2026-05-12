using System.Collections.Concurrent;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services.Translation;
using Microsoft.Extensions.Logging;

namespace Lingarr.Server.Services.Translation;

/// <summary>
/// Thread-safe circuit breaker for translation providers.
/// 
/// States:
/// - Closed: Normal operation. Counts consecutive failures.
///   After reaching the threshold, transitions to Open.
/// - Open: All requests are rejected immediately. After the cooldown
///   period expires, transitions to HalfOpen.
/// - HalfOpen: A single test request is allowed. If it succeeds,
///   the circuit closes. If it fails, the circuit re-opens with
///   an exponentially increasing cooldown (capped at MaxCooldown).
/// 
/// Cooldown starts at InitialCooldown and doubles each cycle,
/// capped at MaxCooldown.
/// </summary>
public class ProviderCircuitBreaker : IProviderCircuitBreaker
{
    private readonly ILogger<ProviderCircuitBreaker> _logger;

    private const int FailureThreshold = 3;
    private static readonly TimeSpan InitialCooldown = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxCooldown = TimeSpan.FromMinutes(10);
    private const int CooldownMultiplier = 2;

    private readonly ConcurrentDictionary<string, CircuitData> _circuits = new();

    public ProviderCircuitBreaker(ILogger<ProviderCircuitBreaker> logger)
    {
        _logger = logger;
    }

    public Task EnsureAllowedAsync(string providerName, CancellationToken cancellationToken)
    {
        var data = _circuits.GetOrAdd(providerName, _ => new CircuitData());

        lock (data.Lock)
        {
            var state = data.State;
            var now = DateTime.UtcNow;

            if (state == CircuitState.Closed)
            {
                return Task.CompletedTask;
            }

            if (state == CircuitState.Open)
            {
                if (now < data.CooldownUntil)
                {
                    var remaining = data.CooldownUntil - now;
                    throw new ProviderCircuitOpenException(providerName, remaining, data.ConsecutiveFailures);
                }

                data.State = CircuitState.HalfOpen;
                _logger.LogInformation(
                    "Circuit breaker for provider '{Provider}' entering half-open state. " +
                    "Allowing a test request after {Failures} consecutive failures.",
                    providerName, data.ConsecutiveFailures);
                return Task.CompletedTask;
            }

            // HalfOpen: one request is allowed through
            return Task.CompletedTask;
        }
    }

    public void RecordSuccess(string providerName)
    {
        var data = _circuits.GetOrAdd(providerName, _ => new CircuitData());

        lock (data.Lock)
        {
            var previousState = data.State;

            data.ConsecutiveFailures = 0;
            data.State = CircuitState.Closed;
            data.CurrentCooldown = null;

            if (previousState != CircuitState.Closed)
            {
                _logger.LogInformation(
                    "Circuit breaker for provider '{Provider}' closed after successful request. " +
                    "Previous state was {PreviousState}.",
                    providerName, previousState);
            }
        }
    }

    public void RecordFailure(string providerName, Exception exception)
    {
        var data = _circuits.GetOrAdd(providerName, _ => new CircuitData());

        lock (data.Lock)
        {
            var previousState = data.State;

            data.ConsecutiveFailures++;

            if (previousState == CircuitState.HalfOpen)
            {
                data.State = CircuitState.Open;
                data.CurrentCooldown = data.CurrentCooldown is null
                    ? InitialCooldown
                    : TimeSpan.FromTicks(Math.Min(
                        data.CurrentCooldown.Value.Ticks * CooldownMultiplier,
                        MaxCooldown.Ticks));
                data.CooldownUntil = DateTime.UtcNow + data.CurrentCooldown.Value;

                _logger.LogWarning(
                    "Circuit breaker for provider '{Provider}' re-opened from half-open. " +
                    "Test request failed. Cooldown: {CooldownSeconds}s. " +
                    "Consecutive failures: {Failures}. Error: {Error}",
                    providerName, data.CurrentCooldown.Value.TotalSeconds,
                    data.ConsecutiveFailures, exception.Message);
                return;
            }

            if (data.ConsecutiveFailures >= FailureThreshold && data.State != CircuitState.Open)
            {
                data.State = CircuitState.Open;
                data.CurrentCooldown = InitialCooldown;
                data.CooldownUntil = DateTime.UtcNow + InitialCooldown;

                _logger.LogWarning(
                    "Circuit breaker for provider '{Provider}' opened after {Failures} consecutive failures. " +
                    "Cooldown: {CooldownSeconds}s. Error: {Error}",
                    providerName, data.ConsecutiveFailures,
                    InitialCooldown.TotalSeconds, exception.Message);
                return;
            }

            if (data.State == CircuitState.Closed)
            {
                _logger.LogDebug(
                    "Circuit breaker for provider '{Provider}' recorded failure {Failures}/{Threshold}. " +
                    "Error: {Error}",
                    providerName, data.ConsecutiveFailures, FailureThreshold, exception.Message);
            }
        }
    }

    public bool IsInCooldown(string providerName)
    {
        var data = _circuits.GetOrAdd(providerName, _ => new CircuitData());

        lock (data.Lock)
        {
            if (data.State == CircuitState.Open)
            {
                return DateTime.UtcNow < data.CooldownUntil;
            }

            return false;
        }
    }

    public CircuitState GetCircuitState(string providerName)
    {
        var data = _circuits.GetOrAdd(providerName, _ => new CircuitData());

        lock (data.Lock)
        {
            if (data.State == CircuitState.Open && DateTime.UtcNow >= data.CooldownUntil)
            {
                return CircuitState.HalfOpen;
            }

            return data.State;
        }
    }

    private class CircuitData
    {
        public CircuitState State = CircuitState.Closed;
        public int ConsecutiveFailures;
        public TimeSpan? CurrentCooldown;
        public DateTime CooldownUntil = DateTime.MinValue;
        public readonly object Lock = new();
    }
}