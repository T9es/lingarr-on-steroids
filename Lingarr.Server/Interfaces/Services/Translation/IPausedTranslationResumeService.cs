namespace Lingarr.Server.Interfaces.Services.Translation;

public interface IPausedTranslationResumeService
{
    Task<int> ResumeDuePausedRequestsAsync(CancellationToken cancellationToken);

    Task<int> ResumePausedRequestsForProviderChangeAsync(CancellationToken cancellationToken);
}
