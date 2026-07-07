using Lingarr.Core.Entities;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Models.Translation;

namespace Lingarr.Server.Interfaces.Services.Translation;

public interface ITranslationSiblingSequenceApprovalService
{
    Task<SiblingSequenceApprovalResult> ProcessMissingTranslationAsync(
        TranslationRequest request,
        MissingTranslationException exception,
        CancellationToken cancellationToken);
}
