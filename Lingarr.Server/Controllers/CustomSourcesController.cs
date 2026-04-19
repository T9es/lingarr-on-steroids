using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CustomSourcesController : ControllerBase
{
    private readonly ICustomSourceService _customSourceService;
    private readonly ICustomSourceScannerService _scannerService;
    private readonly ICustomMediaSubtitleProcessor _customMediaSubtitleProcessor;
    private readonly ISubtitleService _subtitleService;
    private readonly ISettingService _settingService;

    public CustomSourcesController(
        ICustomSourceService customSourceService,
        ICustomSourceScannerService scannerService,
        ICustomMediaSubtitleProcessor customMediaSubtitleProcessor,
        ISubtitleService subtitleService,
        ISettingService settingService)
    {
        _customSourceService = customSourceService;
        _scannerService = scannerService;
        _customMediaSubtitleProcessor = customMediaSubtitleProcessor;
        _subtitleService = subtitleService;
        _settingService = settingService;
    }

    [HttpGet]
    public async Task<ActionResult<List<CustomSource>>> GetSources(CancellationToken cancellationToken)
    {
        return Ok(await _customSourceService.GetSourcesAsync(cancellationToken));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CustomSource>> GetSource(int id, CancellationToken cancellationToken)
    {
        var source = await _customSourceService.GetSourceAsync(id, cancellationToken);
        return source == null ? NotFound() : Ok(source);
    }

    [HttpPost]
    public async Task<ActionResult<CustomSource>> CreateSource([FromBody] CustomSource source, CancellationToken cancellationToken)
    {
        var created = await _customSourceService.CreateSourceAsync(source, cancellationToken);
        return Ok(created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CustomSource>> UpdateSource(int id, [FromBody] CustomSource source, CancellationToken cancellationToken)
    {
        var updated = await _customSourceService.UpdateSourceAsync(id, source, cancellationToken);
        return updated == null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteSource(int id, CancellationToken cancellationToken)
    {
        return await _customSourceService.DeleteSourceAsync(id, cancellationToken) ? Ok() : NotFound();
    }

    [HttpGet("{id:int}/items")]
    public async Task<ActionResult<List<CustomMediaItem>>> GetItems(int id, CancellationToken cancellationToken)
    {
        return Ok(await _customSourceService.GetItemsAsync(id, cancellationToken));
    }

    [HttpPost("{id:int}/rescan")]
    public async Task<IActionResult> RescanSource(int id, CancellationToken cancellationToken)
    {
        await _scannerService.ScanSourceAsync(id, cancellationToken);
        return Ok();
    }

    [HttpPost("rescan")]
    public async Task<IActionResult> RescanAll(CancellationToken cancellationToken)
    {
        await _customSourceService.RescanEnabledSourcesAsync(cancellationToken);
        return Ok();
    }

    [HttpPost("items/{itemId:int}/exclude")]
    public async Task<IActionResult> SetExcluded(int itemId, [FromQuery] bool excluded, CancellationToken cancellationToken)
    {
        return await _customSourceService.SetItemExcludedAsync(itemId, excluded, cancellationToken) ? Ok() : NotFound();
    }

    [HttpPost("items/{itemId:int}/priority")]
    public async Task<IActionResult> SetPriority(int itemId, [FromQuery] bool priority, CancellationToken cancellationToken)
    {
        return await _customSourceService.SetItemPriorityAsync(itemId, priority, cancellationToken) ? Ok() : NotFound();
    }

    [HttpPost("items/{itemId:int}/translate")]
    public async Task<ActionResult<TranslateMediaResponse>> TranslateItem(int itemId, [FromQuery] bool forceRecreate, CancellationToken cancellationToken)
    {
        var item = await _customSourceService.GetItemAsync(itemId, cancellationToken);
        if (item == null)
        {
            return NotFound(new TranslateMediaResponse { Message = "Custom media item not found" });
        }

        var cleanedOutputs = 0;
        if (forceRecreate)
        {
            cleanedOutputs = await CleanupLingarrOutputsForItemAsync(item);
        }

        var queued = await _customMediaSubtitleProcessor.ProcessCustomItemForceAsync(
            item,
            forceProcess: true,
            forceTranslation: forceRecreate,
            forcePriority: true);

        var message = queued > 0 ? $"{queued} translation(s) queued" : "No translations needed";
        if (forceRecreate)
        {
            message += $". Removed {cleanedOutputs} Lingarr-managed subtitle file(s) before recreate.";
        }

        return Ok(new TranslateMediaResponse
        {
            TranslationsQueued = queued,
            Message = message
        });
    }

    private async Task<int> CleanupLingarrOutputsForItemAsync(CustomMediaItem item)
    {
        var directoryPath = Path.GetDirectoryName(item.Path);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return 0;
        }

        var taggingEnabled = await _settingService.GetSetting(Lingarr.Core.Configuration.SettingKeys.Translation.UseSubtitleTagging);
        if (!string.Equals(taggingEnabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var subtitleTag = await _settingService.GetSetting(Lingarr.Core.Configuration.SettingKeys.Translation.SubtitleTag) ?? string.Empty;
        var subtitleTagShort = await _settingService.GetSetting(Lingarr.Core.Configuration.SettingKeys.Translation.SubtitleTagShort) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(subtitleTag) && string.IsNullOrWhiteSpace(subtitleTagShort))
        {
            return 0;
        }

        if (!Directory.Exists(directoryPath))
        {
            return 0;
        }

        var subtitles = await _subtitleService.GetAllSubtitles(directoryPath);
        var mediaFileName = item.FileName;
        var mediaNameNoExt = Path.GetFileNameWithoutExtension(mediaFileName);
        var matchingSubtitles = subtitles
            .Where(subtitle =>
                subtitle.FileName.StartsWith(mediaFileName + ".", StringComparison.OrdinalIgnoreCase) ||
                subtitle.FileName.Equals(mediaFileName, StringComparison.OrdinalIgnoreCase) ||
                subtitle.FileName.StartsWith(mediaNameNoExt + ".", StringComparison.OrdinalIgnoreCase))
            .Where(subtitle =>
                (!string.IsNullOrWhiteSpace(subtitleTag) &&
                 Path.GetFileName(subtitle.Path).Contains(subtitleTag, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(subtitleTagShort) &&
                 Path.GetFileName(subtitle.Path).Contains(subtitleTagShort, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var deletedCount = 0;
        foreach (var subtitle in matchingSubtitles)
        {
            if (!System.IO.File.Exists(subtitle.Path))
            {
                continue;
            }

            try
            {
                System.IO.File.Delete(subtitle.Path);
                deletedCount++;
            }
            catch
            {
                // best-effort cleanup only
            }
        }

        return deletedCount;
    }
}
