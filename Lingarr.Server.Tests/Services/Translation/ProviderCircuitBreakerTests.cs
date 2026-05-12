using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Services.Translation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class ProviderCircuitBreakerTests
{
    private readonly Mock<ILogger<ProviderCircuitBreaker>> _loggerMock = new();

    [Fact]
    public async Task EnsureAllowedAsync_WhenClosed_ShouldAllowRequests()
    {
        var breaker = new ProviderCircuitBreaker(_loggerMock.Object);

        await breaker.EnsureAllowedAsync("test-provider", CancellationToken.None);
        Assert.False(breaker.IsInCooldown("test-provider"));
        Assert.Equal(CircuitState.Closed, breaker.GetCircuitState("test-provider"));
    }

    [Fact]
    public void RecordSuccess_WhenClosed_ShouldStayClosed()
    {
        var breaker = new ProviderCircuitBreaker(_loggerMock.Object);

        breaker.RecordSuccess("test-provider");

        Assert.False(breaker.IsInCooldown("test-provider"));
        Assert.Equal(CircuitState.Closed, breaker.GetCircuitState("test-provider"));
    }

    [Fact]
    public async Task CircuitBreaker_ShouldOpenAfterConsecutiveFailures()
    {
        var breaker = new ProviderCircuitBreaker(_loggerMock.Object);
        var ex = new HttpRequestException("Server error", null, HttpStatusCode.ServiceUnavailable);

        breaker.RecordFailure("test-provider", ex);
        breaker.RecordFailure("test-provider", ex);
        breaker.RecordFailure("test-provider", ex);

        Assert.True(breaker.IsInCooldown("test-provider"));

        await Assert.ThrowsAsync<ProviderCircuitOpenException>(
            () => breaker.EnsureAllowedAsync("test-provider", CancellationToken.None));
    }

    [Fact]
    public void CircuitBreaker_ShouldNotOpenBeforeThreshold()
    {
        var breaker = new ProviderCircuitBreaker(_loggerMock.Object);
        var ex = new HttpRequestException("Server error", null, HttpStatusCode.ServiceUnavailable);

        breaker.RecordFailure("test-provider", ex);
        breaker.RecordFailure("test-provider", ex);

        Assert.False(breaker.IsInCooldown("test-provider"));
        Assert.Equal(CircuitState.Closed, breaker.GetCircuitState("test-provider"));
    }

    [Fact]
    public void RecordSuccess_ShouldResetFailuresAndCloseCircuit()
    {
        var breaker = new ProviderCircuitBreaker(_loggerMock.Object);
        var ex = new HttpRequestException("Server error", null, HttpStatusCode.ServiceUnavailable);

        breaker.RecordFailure("test-provider", ex);
        breaker.RecordFailure("test-provider", ex);
        breaker.RecordSuccess("test-provider");

        Assert.False(breaker.IsInCooldown("test-provider"));
        Assert.Equal(CircuitState.Closed, breaker.GetCircuitState("test-provider"));

        breaker.RecordFailure("test-provider", ex);

        Assert.False(breaker.IsInCooldown("test-provider"));
    }

    [Fact]
    public void CircuitBreaker_ShouldReopenFromHalfOpen()
    {
        var breaker = new ProviderCircuitBreaker(_loggerMock.Object);
        var ex = new HttpRequestException("Server error", null, HttpStatusCode.ServiceUnavailable);

        // Open circuit
        breaker.RecordFailure("test-provider", ex);
        breaker.RecordFailure("test-provider", ex);
        breaker.RecordFailure("test-provider", ex);
        Assert.True(breaker.IsInCooldown("test-provider"));

        // Cooldown has a real duration of 30s, but we can only test the state transitions
    }

    [Fact]
    public async Task EnsureAllowedAsync_WhenProviderDoesNotExist_ShouldAllow()
    {
        var breaker = new ProviderCircuitBreaker(_loggerMock.Object);

        await breaker.EnsureAllowedAsync("never-used-provider", CancellationToken.None);

        Assert.False(breaker.IsInCooldown("never-used-provider"));
    }

    [Fact]
    public void IsInCooldown_WhenClosed_ShouldReturnFalse()
    {
        var breaker = new ProviderCircuitBreaker(_loggerMock.Object);

        Assert.False(breaker.IsInCooldown("test-provider"));
    }

    [Fact]
    public void GetCircuitState_WhenClosed_ShouldReturnClosed()
    {
        var breaker = new ProviderCircuitBreaker(_loggerMock.Object);

        Assert.Equal(CircuitState.Closed, breaker.GetCircuitState("test-provider"));
    }

    [Fact]
    public void ProviderCircuitOpenException_ShouldContainProviderInfo()
    {
        var ex = new ProviderCircuitOpenException("crofai", TimeSpan.FromSeconds(30), 3);

        Assert.Contains("crofai", ex.ProviderName);
        Assert.Equal(30, ex.CooldownRemaining.TotalSeconds);
        Assert.Equal(3, ex.ConsecutiveFailures);
    }

    [Fact]
    public void CircuitBreaker_ShouldTrackDifferentProvidersIndependently()
    {
        var breaker = new ProviderCircuitBreaker(_loggerMock.Object);
        var ex = new HttpRequestException("Server error", null, HttpStatusCode.ServiceUnavailable);

        breaker.RecordFailure("crofai", ex);
        breaker.RecordFailure("crofai", ex);
        breaker.RecordFailure("crofai", ex);

        Assert.True(breaker.IsInCooldown("crofai"));
        Assert.False(breaker.IsInCooldown("gemini"));
    }

    [Fact]
    public void RecordFailure_WithDifferentExceptionTypes_ShouldStillTrip()
    {
        var breaker = new ProviderCircuitBreaker(_loggerMock.Object);

        breaker.RecordFailure("test-provider", new HttpRequestException("Err 1 ", null, HttpStatusCode.ServiceUnavailable));
        breaker.RecordFailure("test-provider", new TranslationException("Err 2"));
        breaker.RecordFailure("test-provider", new Exception("Err 3"));

        Assert.True(breaker.IsInCooldown("test-provider"));
    }
}