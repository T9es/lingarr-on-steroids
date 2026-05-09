using Hangfire;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services.Subtitle;

namespace Lingarr.Server.Jobs;

public class SubtitleOcrJob
{
    private readonly ISubtitleOcrService _subtitleOcrService;
    private readonly ILogger<SubtitleOcrJob> _logger;

    public SubtitleOcrJob(
        ISubtitleOcrService subtitleOcrService,
        ILogger<SubtitleOcrJob> logger)
    {
        _subtitleOcrService = subtitleOcrService;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    [Queue("system")]
    public async Task Execute(int mediaId, MediaType mediaType, int streamIndex, bool manual)
    {
        var result = await _subtitleOcrService.RunOcrAsync(mediaId, mediaType, streamIndex, manual);
        if (!result.Success)
        {
            _logger.LogWarning(
                "Subtitle OCR did not produce an auto-accepted source for {MediaType} {MediaId} stream {StreamIndex}: {Status} {Error}",
                mediaType,
                mediaId,
                streamIndex,
                result.Status,
                result.Error ?? result.IssueSummary);
        }
    }
}
