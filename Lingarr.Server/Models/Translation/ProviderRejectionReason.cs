namespace Lingarr.Server.Models.Translation;

/// <summary>
/// Stable rejection reasons attached to provider translations that failed validation.
/// The reasons are persisted with the failed cue and surfaced to the user so a human
/// can accept a rejected translation variant explicitly.
/// </summary>
public static class ProviderRejectionReason
{
    /// <summary>The provider returned the source text unchanged.</summary>
    public const string SourceEcho = "source-echo";

    /// <summary>The provider output was classified as the wrong target language or as mojibake.</summary>
    public const string WrongTargetLanguage = "wrong-target-language";

    /// <summary>The provider returned an empty or otherwise unusable translation.</summary>
    public const string InvalidTranslation = "invalid-translation";
}
