using Lingarr.Server.Models;

namespace Lingarr.Server.Interfaces.Services.Subtitle;

public interface ISubtitleQualityValidatorService
{
    Task<SubtitleQualityValidationResult> ValidateAsync(
        SubtitleQualityValidationRequest request,
        CancellationToken cancellationToken);
}
