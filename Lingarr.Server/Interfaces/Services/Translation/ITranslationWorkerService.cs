using Lingarr.Core.Enum;

namespace Lingarr.Server.Interfaces.Services.Translation;

/// <summary>
/// Service that manages background translation workers.
/// Replaces Hangfire for translation job processing with a custom BackgroundService.
/// </summary>
public interface ITranslationWorkerService
{
    /// <summary>
    /// Current number of actively running translation workers.
    /// </summary>
    int ActiveWorkers { get; }
    
    /// <summary>
    /// Maximum allowed concurrent translations (from settings, capped at 20).
    /// </summary>
    int MaxWorkers { get; }
    
    /// <summary>
    /// Dynamically adjust the number of workers.
    /// Takes effect immediately - no restart required.
    /// </summary>
    /// <param name="maxWorkers">New maximum worker count (will be clamped to 1-20)</param>
    Task ReconfigureWorkersAsync(int maxWorkers);
    
    /// <summary>
    /// Signal that new work is available.
    /// Optimization to avoid waiting for the next poll interval.
    /// </summary>
    void Signal();
}
