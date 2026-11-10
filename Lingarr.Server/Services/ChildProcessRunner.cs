using System.ComponentModel;
using System.Diagnostics;

namespace Lingarr.Server.Services;

/// <summary>
/// Runs an external media tool with a hard timeout and guarantees the child process tree is
/// terminated on timeout, cancellation, or an unexpected failure.
/// </summary>
/// <remarks>
/// A hung tool must never outlive the job that started it. A single orphaned process pinned a
/// production core for five days, held the job's in-memory locks for the same period, and made
/// every later run that needed the same output deadlock. Awaiting <see cref="Process.WaitForExitAsync"/>
/// without a timeout is not enough: the wait never completes, so any cleanup placed after it is
/// unreachable.
/// </remarks>
internal static class ChildProcessRunner
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan KillWaitTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The outcome of a child process run.
    /// </summary>
    /// <param name="ExitCode">The process exit code, or -1 when the process was terminated.</param>
    /// <param name="StandardOutput">Captured standard output.</param>
    /// <param name="StandardError">Captured standard error.</param>
    /// <param name="TimedOut">True when the process exceeded the timeout and was killed.</param>
    internal sealed record Result(int ExitCode, string StandardOutput, string StandardError, bool TimedOut)
    {
        /// <summary>
        /// True when the process exited successfully without timing out.
        /// </summary>
        public bool Succeeded => !TimedOut && ExitCode == 0;
    }

    /// <summary>
    /// Starts the process, waits for it to exit within <paramref name="timeout"/>, and kills the
    /// whole process tree when it does not finish in time or the caller cancels.
    /// </summary>
    /// <param name="startInfo">
    /// The process to start. Standard output and standard error must be redirected.
    /// </param>
    /// <param name="timeout">The maximum time the process may run.</param>
    /// <param name="cancellationToken">Cancels the wait and kills the process tree.</param>
    /// <returns>The captured result. A timed out run reports <see cref="Result.TimedOut"/>.</returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown after the child is killed when <paramref name="cancellationToken"/> is cancelled.
    /// </exception>
    internal static async Task<Result> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The timeout must be positive.");
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Could not start '{startInfo.FileName}': {ex.Message}",
                ex);
        }

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                KillProcessTree(process);
                var (timedOutStdout, timedOutStderr) = await DrainAsync(stdoutTask, stderrTask)
                    .ConfigureAwait(false);

                // Cancellation is a signal rather than a failure result: let it propagate so the
                // caller sees a cancellation instead of a generic "process failed" message.
                cancellationToken.ThrowIfCancellationRequested();

                return new Result(-1, timedOutStdout, timedOutStderr, TimedOut: true);
            }

            return new Result(
                process.ExitCode,
                await stdoutTask.ConfigureAwait(false),
                await stderrTask.ConfigureAwait(false),
                TimedOut: false);
        }
        finally
        {
            // Covers every exit path, including an exception while draining the pipes: the child
            // must never survive the call that owns it.
            if (!HasExited(process))
            {
                KillProcessTree(process);
            }
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited between the check and the kill.
        }
        catch (Win32Exception)
        {
            // The OS refused the kill (already reaped or not permitted).
        }
        finally
        {
            try
            {
                process.WaitForExit(KillWaitTimeout);
            }
            catch (InvalidOperationException)
            {
                // The process was never started; nothing to wait for.
            }
        }
    }

    private static async Task<(string StandardOutput, string StandardError)> DrainAsync(
        Task<string> stdoutTask,
        Task<string> stderrTask)
    {
        var combined = Task.WhenAll(IgnoreFaultsAsync(stdoutTask), IgnoreFaultsAsync(stderrTask));
        var completed = await Task.WhenAny(combined, Task.Delay(DrainTimeout)).ConfigureAwait(false);
        if (completed != combined)
        {
            return (string.Empty, string.Empty);
        }

        return (SafeResult(stdoutTask), SafeResult(stderrTask));
    }

    private static async Task IgnoreFaultsAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // The pipe may already be closed by the kill; the output is simply lost.
        }
    }

    private static string SafeResult(Task<string> task)
    {
        try
        {
            return task.IsCompletedSuccessfully ? task.Result : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
