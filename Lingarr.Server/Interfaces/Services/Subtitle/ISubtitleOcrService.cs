using Lingarr.Core.Enum;
using Lingarr.Server.Models.Subtitle;

namespace Lingarr.Server.Interfaces.Services.Subtitle;

public interface ISubtitleOcrService
{
    bool IsSupportedCodec(string? codecName);

    Task<SubtitleOcrResult> QueueOcrAsync(
        int mediaId,
        MediaType mediaType,
        int streamIndex,
        bool manual,
        CancellationToken cancellationToken = default);

    Task<SubtitleOcrResult> RunOcrAsync(
        int mediaId,
        MediaType mediaType,
        int streamIndex,
        bool manual,
        CancellationToken cancellationToken = default);

    Task<SubtitleOcrResult> ApproveOcrAsync(
        int mediaId,
        MediaType mediaType,
        int streamIndex,
        CancellationToken cancellationToken = default);

    Task<SubtitleOcrPreviewResponse> GetPreviewAsync(
        int mediaId,
        MediaType mediaType,
        int streamIndex,
        CancellationToken cancellationToken = default);
}
