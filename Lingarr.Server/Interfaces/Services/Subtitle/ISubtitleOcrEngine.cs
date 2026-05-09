using Lingarr.Server.Models.Subtitle;

namespace Lingarr.Server.Interfaces.Services.Subtitle;

public interface ISubtitleOcrEngine
{
    Task<SubtitleOcrEngineResult> ConvertAsync(
        string mediaPath,
        int subtitleStreamIndex,
        string outputPath,
        string tesseractLanguage,
        CancellationToken cancellationToken = default);
}
