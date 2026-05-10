namespace Lingarr.Server.Interfaces.Services.Subtitle;

public interface ISubtitleLanguageDetectionService
{
    Task<int> DetectUnknownLanguagesAsync(int? movieId = null, int? episodeId = null, CancellationToken ct = default);
}