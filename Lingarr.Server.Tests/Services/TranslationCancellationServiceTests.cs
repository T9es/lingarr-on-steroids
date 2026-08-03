using System.Threading;
using System.Threading.Tasks;
using Lingarr.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class TranslationCancellationServiceTests
{
    [Fact]
    public void RegisterJobWithOwnership_KeepsAttemptsIndependentWithoutAllowingOldCleanupToTouchNewerAttempt()
    {
        var service = new TranslationCancellationService(
            NullLogger<TranslationCancellationService>.Instance);

        var oldToken = service.RegisterJob(3, "old-attempt");
        var replacementToken = service.RegisterJob(3, "replacement-attempt");

        Assert.NotEqual(oldToken, replacementToken);
        Assert.False(oldToken.IsCancellationRequested);
        Assert.Equal(replacementToken, service.GetToken(3, "replacement-attempt"));
        Assert.Equal(oldToken, service.GetToken(3, "old-attempt"));
        Assert.True(service.UnregisterJob(3, oldToken));
        Assert.False(service.CancelJob(3, oldToken));
        Assert.False(replacementToken.IsCancellationRequested);
        Assert.True(service.UnregisterJob(3, replacementToken));
    }

    [Fact]
    public async Task DelayedOldAttemptRegistration_DoesNotReplaceNewerAttemptToken()
    {
        var service = new TranslationCancellationService(
            NullLogger<TranslationCancellationService>.Instance);
        var allowOldRegistration = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var delayedOldRegistration = Task.Run(async () =>
        {
            await allowOldRegistration.Task;
            return service.RegisterJob(4, "old-attempt");
        });

        var newerToken = service.RegisterJob(4, "new-attempt");
        allowOldRegistration.SetResult(true);
        var oldToken = await delayedOldRegistration;

        Assert.NotEqual(oldToken, newerToken);
        Assert.Equal(newerToken, service.GetToken(4, "new-attempt"));
        Assert.Equal(oldToken, service.GetToken(4, "old-attempt"));
        Assert.True(service.CancelJob(4, oldToken));
        Assert.True(oldToken.IsCancellationRequested);
        Assert.False(newerToken.IsCancellationRequested);
        Assert.True(service.UnregisterJob(4, oldToken));
        Assert.True(service.UnregisterJob(4, newerToken));
    }

    [Fact]
    public void CancelJobWithCapturedToken_DoesNotCancelReplacementRegistration()
    {
        var service = new TranslationCancellationService(
            NullLogger<TranslationCancellationService>.Instance);

        var originalToken = service.RegisterJob(1);
        service.UnregisterJob(1);
        var replacementToken = service.RegisterJob(1);

        Assert.False(service.CancelJob(1, originalToken));
        Assert.False(replacementToken.IsCancellationRequested);

        service.UnregisterJob(1);
    }

    [Fact]
    public void CancelJobWithCapturedToken_CancelsOnlyTheCapturedRegistration()
    {
        var service = new TranslationCancellationService(
            NullLogger<TranslationCancellationService>.Instance);

        var token = service.RegisterJob(2);

        Assert.True(service.CancelJob(2, token));
        Assert.True(token.IsCancellationRequested);

        service.UnregisterJob(2);
    }
}
