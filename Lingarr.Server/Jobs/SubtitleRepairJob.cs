using Hangfire;
using Lingarr.Server.Filters;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Microsoft.OpenApi.Extensions;

namespace Lingarr.Server.Jobs;

/// <summary>
/// Background job that scans Completed translation requests with missing
/// TranslatedSubtitle paths and attempts to repair them.
/// </summary>
public class SubtitleRepairJob
{
    private readonly ITranslationSubtitleRepairService _repairService;
    private readonly ILogger<SubtitleRepairJob> _logger;

    public SubtitleRepairJob(
        ITranslationSubtitleRepairService repairService,
        ILogger<SubtitleRepairJob> logger)
    {
        _repairService = repairService;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
    [AutomaticRetry(Attempts = 0)]
    [Queue("system")]
    public async Task Execute()
    {
        var jobName = JobContextFilter.GetCurrentJobTypeName();

        _logger.LogInformation("Starting subtitle orphan repair job...");

        try
        {
            var result = await _repairService.RepairOrphanedRecordsAsync();

            _logger.LogInformation("Subtitle repair job completed: {Summary}", result.Summary);

            if (result.Unfixable > 0)
            {
                _logger.LogWarning(
                    "{Unfixable} records could not be auto-repaired. Details: {Details}",
                    result.Unfixable,
                    string.Join("; ", result.Details));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subtitle repair job failed");
            throw;
        }
    }
}
