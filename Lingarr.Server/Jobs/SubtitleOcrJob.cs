using Hangfire;
using Lingarr.Core.Data;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Jobs;

public class SubtitleOcrJob
{
    private readonly ISubtitleOcrService _subtitleOcrService;
    private readonly IMediaSubtitleProcessor _mediaSubtitleProcessor;
    private readonly LingarrDbContext _dbContext;
    private readonly ILogger<SubtitleOcrJob> _logger;

    public SubtitleOcrJob(
        ISubtitleOcrService subtitleOcrService,
        IMediaSubtitleProcessor mediaSubtitleProcessor,
        LingarrDbContext dbContext,
        ILogger<SubtitleOcrJob> logger)
    {
        _subtitleOcrService = subtitleOcrService;
        _mediaSubtitleProcessor = mediaSubtitleProcessor;
        _dbContext = dbContext;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
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
            return;
        }

        var media = await LoadMediaAsync(mediaId, mediaType);
        if (media == null)
        {
            _logger.LogWarning(
                "Subtitle OCR passed for {MediaType} {MediaId} stream {StreamIndex}, but the media item could not be found for translation handoff.",
                mediaType,
                mediaId,
                streamIndex);
            return;
        }

        var queued = await _mediaSubtitleProcessor.ProcessMediaForceAsync(
            media,
            mediaType,
            forceProcess: true,
            forceTranslation: false);
        _logger.LogInformation(
            "Subtitle OCR passed for {MediaType} {MediaId} stream {StreamIndex}; queued {QueuedCount} translation request(s).",
            mediaType,
            mediaId,
            streamIndex,
            queued);
    }

    private async Task<IMedia?> LoadMediaAsync(int mediaId, MediaType mediaType)
    {
        return mediaType == MediaType.Movie
            ? await _dbContext.Movies.FirstOrDefaultAsync(movie => movie.Id == mediaId)
            : await _dbContext.Episodes.FirstOrDefaultAsync(episode => episode.Id == mediaId);
    }
}
