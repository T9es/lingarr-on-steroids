using System;
using Lingarr.Server.Services.Translation;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class RetryDelayPolicyTests
{
    [Fact]
    public void Next_DoublesTheDelay()
    {
        var next = RetryDelayPolicy.Next(TimeSpan.FromSeconds(5), 2);

        Assert.Equal(TimeSpan.FromSeconds(10), next);
    }

    [Fact]
    public void Next_RepeatedBackoffStaysBounded()
    {
        var delay = TimeSpan.FromSeconds(5);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            delay = RetryDelayPolicy.Next(delay, 2);
        }

        Assert.True(
            delay <= RetryDelayPolicy.MaximumDelay,
            $"Backoff reached {delay}, which exceeds the {RetryDelayPolicy.MaximumDelay} ceiling.");
    }

    [Fact]
    public void Next_DoesNotGrowBeyondTheMaximum()
    {
        var next = RetryDelayPolicy.Next(TimeSpan.FromMinutes(1), 2);

        Assert.Equal(RetryDelayPolicy.MaximumDelay, next);
    }

    [Fact]
    public void Next_NeverReturnsLessThanTheMinimum()
    {
        Assert.Equal(RetryDelayPolicy.MinimumDelay, RetryDelayPolicy.Next(TimeSpan.Zero, 2));
        Assert.Equal(
            RetryDelayPolicy.MinimumDelay,
            RetryDelayPolicy.Next(TimeSpan.FromMilliseconds(10), 1));
    }
}
