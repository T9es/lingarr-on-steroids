namespace Lingarr.Server.Services.Translation;

/// <summary>
/// Bounds the retry backoff used by the translation providers.
/// </summary>
/// <remarks>
/// Each provider multiplies its delay after every failed attempt. With the shipped defaults
/// (five seconds, multiplier two, twenty retries) the last wait is roughly thirty days, which
/// looks like a hung translation job and pins the worker that owns it. The delay has to stay
/// short enough that a failing request finishes in bounded time.
/// </remarks>
internal static class RetryDelayPolicy
{
    /// <summary>
    /// The longest wait between two attempts.
    /// </summary>
    internal static readonly TimeSpan MaximumDelay = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The shortest wait between two attempts, so a zero or one multiplier cannot spin.
    /// </summary>
    internal static readonly TimeSpan MinimumDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Returns the next retry delay, never below <see cref="MinimumDelay"/> and never above
    /// <see cref="MaximumDelay"/>.
    /// </summary>
    /// <param name="currentDelay">The delay used before the previous attempt.</param>
    /// <param name="multiplier">The configured growth multiplier.</param>
    internal static TimeSpan Next(TimeSpan currentDelay, int multiplier)
    {
        if (currentDelay <= TimeSpan.Zero)
        {
            return MinimumDelay;
        }

        if (currentDelay >= MaximumDelay)
        {
            return MaximumDelay;
        }

        var factor = Math.Clamp(multiplier, 1, 10);
        var scaledTicks = (double)currentDelay.Ticks * factor;
        var boundedTicks = Math.Clamp(
            (long)Math.Min(scaledTicks, MaximumDelay.Ticks),
            MinimumDelay.Ticks,
            MaximumDelay.Ticks);
        return TimeSpan.FromTicks(boundedTicks);
    }
}
