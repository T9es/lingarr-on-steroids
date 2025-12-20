using Lingarr.Server.Models.Chutes;

namespace Lingarr.Server.Interfaces.Services.Translation;

public interface IChutesUsageService
{
    /// <summary>
    /// Ensures a request can be executed without exceeding configured limits.
    /// </summary>
    Task EnsureRequestAllowedAsync(string? modelId, CancellationToken cancellationToken);

    /// <summary>
    /// Records a successful request.
    /// </summary>
    Task RecordRequestAsync(string? modelId, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves a usage snapshot for the configured model.
    /// </summary>
    Task<ChutesUsageSnapshot> GetUsageSnapshotAsync(string? modelId, bool forceRefresh, CancellationToken cancellationToken);

    /// <summary>
    /// Signals that a 402 PaymentRequired error was received, triggering a pause
    /// on all Chutes translations until credits are available again.
    /// </summary>
    void NotifyPaymentRequired();
}
