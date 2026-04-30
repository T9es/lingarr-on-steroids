using Lingarr.Core.Configuration;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DiagnosticsController : ControllerBase
{
    private readonly ITranslationDiagnosticsService _diagnosticsService;
    private readonly ISettingService _settingService;

    public DiagnosticsController(
        ITranslationDiagnosticsService diagnosticsService,
        ISettingService settingService)
    {
        _diagnosticsService = diagnosticsService;
        _settingService = settingService;
    }

    [HttpGet("events")]
    public async Task<ActionResult> GetEvents(
        [FromQuery] int pageSize = 100,
        [FromQuery] int pageNumber = 1,
        CancellationToken cancellationToken = default)
    {
        var events = await _diagnosticsService.GetEventsAsync(
            pageSize,
            pageNumber,
            cancellationToken);
        return Ok(events);
    }

    [HttpGet("media/{mediaType}/{mediaId:int}")]
    public async Task<ActionResult> GetMediaEvents(
        MediaType mediaType,
        int mediaId,
        CancellationToken cancellationToken = default)
    {
        var events = await _diagnosticsService.GetForMediaAsync(
            mediaType,
            mediaId,
            cancellationToken);
        return Ok(events);
    }

    [HttpGet("requests/{requestId:int}")]
    public async Task<ActionResult> GetRequestEvents(
        int requestId,
        CancellationToken cancellationToken = default)
    {
        var events = await _diagnosticsService.GetForRequestAsync(
            requestId,
            cancellationToken);
        return Ok(events);
    }

    [HttpGet("safety-settings")]
    public async Task<ActionResult> GetSafetySettings()
    {
        return Ok(await GetSafetySettingsResponse());
    }

    [HttpPost("apply-safe-settings")]
    public async Task<ActionResult> ApplySafeSettings()
    {
        await _settingService.SetSetting(
            SettingKeys.SubtitleValidation.BulkIntegrityAutoQueue,
            "false");
        await _settingService.SetSetting(
            SettingKeys.SubtitleValidation.BulkIntegrityMaxAutoQueuePerRun,
            "25");
        await _settingService.SetSetting(
            SettingKeys.SubtitleValidation.IntegrityValidationEnabled,
            "true");

        return Ok(await GetSafetySettingsResponse());
    }

    private async Task<object> GetSafetySettingsResponse()
    {
        var autoQueue = await _settingService.GetSetting(
            SettingKeys.SubtitleValidation.BulkIntegrityAutoQueue) ?? "false";
        var maxAutoQueue = await _settingService.GetSetting(
            SettingKeys.SubtitleValidation.BulkIntegrityMaxAutoQueuePerRun) ?? "25";
        var integrityEnabled = await _settingService.GetSetting(
            SettingKeys.SubtitleValidation.IntegrityValidationEnabled) ?? "false";

        return new
        {
            BulkIntegrityAutoQueue = autoQueue,
            BulkIntegrityMaxAutoQueuePerRun = maxAutoQueue,
            IntegrityValidationEnabled = integrityEnabled,
            IsSafe = autoQueue.Equals("false", StringComparison.OrdinalIgnoreCase) &&
                     maxAutoQueue == "25" &&
                     integrityEnabled.Equals("true", StringComparison.OrdinalIgnoreCase)
        };
    }
}
