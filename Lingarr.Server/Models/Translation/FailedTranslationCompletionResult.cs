namespace Lingarr.Server.Models.Translation;

public sealed record FailedTranslationCompletionResult(
    bool Completed,
    bool AlreadyCompleted,
    string? OutputPath,
    string? SkippedReason = null);
