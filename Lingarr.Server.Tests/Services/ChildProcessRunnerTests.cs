using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Server.Services;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class ChildProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_WhenProcessExitsSuccessfully_CapturesOutput()
    {
        var result = await ChildProcessRunner.RunAsync(
            ShellStartInfo("echo child-process-runner-ok"),
            TimeSpan.FromSeconds(30),
            CancellationToken.None);

        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Succeeded);
        Assert.Contains("child-process-runner-ok", result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_WhenProcessExitsNonZero_ReportsExitCodeWithoutTimingOut()
    {
        var result = await ChildProcessRunner.RunAsync(
            ShellStartInfo("exit 7"),
            TimeSpan.FromSeconds(30),
            CancellationToken.None);

        Assert.False(result.TimedOut);
        Assert.Equal(7, result.ExitCode);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RunAsync_WhenProcessHangs_ReturnsTimedOutPromptly()
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await ChildProcessRunner.RunAsync(
            BlockingStartInfo(),
            TimeSpan.FromSeconds(1),
            CancellationToken.None);
        stopwatch.Stop();

        Assert.True(result.TimedOut);
        Assert.False(result.Succeeded);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(20),
            $"The runner waited {stopwatch.Elapsed} instead of enforcing the timeout.");
    }

    [Fact]
    public async Task RunAsync_WhenProcessHangs_KillsTheChildBeforeItFinishesItsWork()
    {
        var marker = Path.Combine(
            Path.GetTempPath(),
            $"child-process-runner-{Guid.NewGuid():N}.txt");

        var result = await ChildProcessRunner.RunAsync(
            DelayedWriteStartInfo(marker),
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.True(result.TimedOut);

        // The child creates the marker only after its sleep completes. If the kill failed, the
        // marker appears within the next few seconds and this assertion catches the orphan.
        await Task.Delay(TimeSpan.FromSeconds(6));
        Assert.False(
            File.Exists(marker),
            "The child process survived the kill and finished its work.");
    }

    [Fact]
    public async Task RunAsync_WhenCallerCancels_ThrowsAndDoesNotWaitForTheChild()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ChildProcessRunner.RunAsync(
                BlockingStartInfo(),
                TimeSpan.FromMinutes(10),
                cts.Token));

        stopwatch.Stop();
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(20),
            $"The runner waited {stopwatch.Elapsed} instead of stopping on cancellation.");
    }

    private static ProcessStartInfo ShellStartInfo(string command)
    {
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/c {command}" }
            : new ProcessStartInfo { FileName = "/bin/sh", Arguments = $"-c \"{command}\"" };

        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        return startInfo;
    }

    private static ProcessStartInfo BlockingStartInfo() =>
        ShellStartInfo(OperatingSystem.IsWindows()
            ? "ping -n 60 127.0.0.1 >nul"
            : "sleep 60");

    private static ProcessStartInfo DelayedWriteStartInfo(string markerPath) =>
        ShellStartInfo(OperatingSystem.IsWindows()
            ? $"ping -n 4 127.0.0.1 >nul & echo done> \"{markerPath}\""
            : $"sleep 3; printf done > '{markerPath}'");
}
