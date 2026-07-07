using Lingarr.Server.Exceptions;

namespace Lingarr.Server.Models.Translation;

public sealed class SiblingSequenceApprovalResult
{
    public bool CurrentRequestCompleted { get; init; }
    public IReadOnlySet<int> ApprovedPositions { get; init; } = new HashSet<int>();
    public IReadOnlyList<int> CompletedRequestIds { get; init; } = [];
    public MissingTranslationException? RemainingException { get; init; }
}
