using System.Collections.Concurrent;
using Lingarr.Server.Interfaces.Services;

namespace Lingarr.Server.Services;

/// <summary>
/// Singleton service that maintains cancellation tokens for running translation jobs,
/// enabling cooperative cancellation when jobs are cancelled from the UI.
/// </summary>
public class TranslationCancellationService : ITranslationCancellationService
{
    private readonly record struct RegistrationKey(int RequestId, string? OwnershipToken);

    private sealed class CancellationRegistration
    {
        public CancellationRegistration(CancellationTokenSource source, string? ownershipToken)
        {
            Source = source;
            Token = source.Token;
            OwnershipToken = ownershipToken;
        }

        public CancellationTokenSource Source { get; }

        public CancellationToken Token { get; }

        public string? OwnershipToken { get; }
    }

    private readonly ConcurrentDictionary<RegistrationKey, CancellationRegistration> _registrations = new();
    private readonly ILogger<TranslationCancellationService> _logger;

    public TranslationCancellationService(ILogger<TranslationCancellationService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public CancellationToken RegisterJob(int requestId)
    {
        return RegisterJobCore(new RegistrationKey(requestId, null));
    }

    /// <inheritdoc />
    public CancellationToken RegisterJob(int requestId, string ownershipToken)
    {
        if (string.IsNullOrWhiteSpace(ownershipToken))
        {
            throw new ArgumentException("An ownership token is required for an attempt-scoped registration.", nameof(ownershipToken));
        }

        return RegisterJobCore(new RegistrationKey(requestId, ownershipToken));
    }

    private CancellationToken RegisterJobCore(RegistrationKey key)
    {
        var registration = new CancellationRegistration(
            new CancellationTokenSource(),
            key.OwnershipToken);

        while (true)
        {
            if (_registrations.TryGetValue(key, out var existing))
            {
                registration.Source.Dispose();
                return existing.Token;
            }

            if (_registrations.TryAdd(key, registration))
            {
                _logger.LogDebug(
                    "Registered cancellation token for request {RequestId}{AttemptScope}",
                    key.RequestId,
                    key.OwnershipToken == null ? string.Empty : " under a worker attempt");
                return registration.Token;
            }
        }
    }

    /// <inheritdoc />
    public CancellationToken GetToken(int requestId)
    {
        if (_registrations.TryGetValue(new RegistrationKey(requestId, null), out var legacyRegistration))
        {
            return legacyRegistration.Token;
        }

        var activeRegistrations = GetAttemptRegistrations(requestId)
            .Where(registration => !registration.Source.IsCancellationRequested)
            .ToList();
        return activeRegistrations.Count == 1
            ? activeRegistrations[0].Token
            : CancellationToken.None;
    }

    /// <inheritdoc />
    public CancellationToken GetToken(int requestId, string? expectedOwnershipToken)
    {
        return _registrations.TryGetValue(
                   new RegistrationKey(requestId, expectedOwnershipToken),
                   out var registration)
            ? registration.Token
            : CancellationToken.None;
    }

    /// <inheritdoc />
    public bool CancelJob(int requestId)
    {
        if (_registrations.TryGetValue(new RegistrationKey(requestId, null), out var legacyRegistration))
        {
            return CancelRegistration(requestId, legacyRegistration, attemptScoped: false);
        }

        var activeRegistrations = GetAttemptRegistrations(requestId)
            .Where(registration => !registration.Source.IsCancellationRequested)
            .ToList();
        return activeRegistrations.Count == 1 &&
               CancelRegistration(requestId, activeRegistrations[0], attemptScoped: true);
    }

    /// <inheritdoc />
    public bool CancelJob(int requestId, CancellationToken expectedToken)
    {
        if (expectedToken == CancellationToken.None)
        {
            return false;
        }

        return TryFindRegistrationByToken(requestId, expectedToken, out _, out var registration) &&
               CancelRegistration(requestId, registration, attemptScoped: true);
    }

    private bool CancelRegistration(
        int requestId,
        CancellationRegistration registration,
        bool attemptScoped)
    {
        try
        {
            registration.Source.Cancel();
            _logger.LogInformation(
                "Triggered cancellation for request {RequestId}{AttemptScope}",
                requestId,
                attemptScoped ? " for the captured attempt" : string.Empty);
            return true;
        }
        catch (ObjectDisposedException)
        {
            // Token was already disposed (job finished), safe to ignore
            _logger.LogDebug("Cancellation token for request {RequestId} was already disposed", requestId);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cancellation callbacks for request {RequestId} threw", requestId);
        }

        return false;
    }

    private IReadOnlyCollection<CancellationRegistration> GetAttemptRegistrations(int requestId)
    {
        return _registrations
            .Where(pair => pair.Key.RequestId == requestId && pair.Key.OwnershipToken != null)
            .Select(pair => pair.Value)
            .ToList();
    }

    private bool TryFindRegistrationByToken(
        int requestId,
        CancellationToken expectedToken,
        out RegistrationKey key,
        out CancellationRegistration registration)
    {
        foreach (var pair in _registrations)
        {
            if (pair.Key.RequestId == requestId && pair.Value.Token == expectedToken)
            {
                key = pair.Key;
                registration = pair.Value;
                return true;
            }
        }

        key = default;
        registration = null!;
        _logger.LogDebug(
            "Skipped cancellation for request {RequestId} because its captured attempt is no longer registered",
            requestId);
        return false;
    }

    private bool TryRemove(RegistrationKey key, CancellationRegistration registration)
    {
        return ((ICollection<KeyValuePair<RegistrationKey, CancellationRegistration>>)_registrations)
            .Remove(new KeyValuePair<RegistrationKey, CancellationRegistration>(key, registration));
    }

    private void DisposeRegistration(int requestId, CancellationRegistration registration)
    {
        try
        {
            registration.Source.Dispose();
            _logger.LogDebug("Unregistered cancellation token for request {RequestId}", requestId);
        }
        catch (ObjectDisposedException)
        {
            // Already disposed, safe to ignore
        }
    }

    /// <inheritdoc />
    public void UnregisterJob(int requestId)
    {
        var legacyKey = new RegistrationKey(requestId, null);
        if (_registrations.TryGetValue(legacyKey, out var legacyRegistration) &&
            TryRemove(legacyKey, legacyRegistration))
        {
            DisposeRegistration(requestId, legacyRegistration);
            return;
        }

        var attemptRegistrations = _registrations
            .Where(pair => pair.Key.RequestId == requestId && pair.Key.OwnershipToken != null)
            .ToList();
        if (attemptRegistrations.Count == 1 &&
            TryRemove(attemptRegistrations[0].Key, attemptRegistrations[0].Value))
        {
            DisposeRegistration(requestId, attemptRegistrations[0].Value);
        }
    }

    /// <inheritdoc />
    public bool UnregisterJob(int requestId, CancellationToken expectedToken)
    {
        if (expectedToken == CancellationToken.None ||
            !TryFindRegistrationByToken(requestId, expectedToken, out var key, out var registration) ||
            !TryRemove(key, registration))
        {
            return false;
        }

        DisposeRegistration(requestId, registration);
        return true;
    }
}
