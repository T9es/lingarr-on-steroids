using Lingarr.Core.Entities;
using Lingarr.Server.Services.Subtitle;

namespace Lingarr.Server.Interfaces.Services.Subtitle;

/// <summary>
/// One subtitle stream to check for a declared-versus-actual language contradiction.
/// </summary>
public sealed record SubtitleLanguageCheckRequest(
    EmbeddedSubtitle Subtitle,
    string MediaFilePath,
    bool AllowLlmRefinement = true,
    bool ForceRefresh = false);

/// <summary>
/// Result of a language contradiction check, including negative verdicts so the reason can
/// be surfaced instead of silently trusting the container tag.
/// </summary>
public sealed record SubtitleLanguageCheckOutcome(
    EmbeddedSubtitle Subtitle,
    SubtitleLanguageVerdict Verdict,
    string? DeclaredLanguage,
    string? DetectedLanguage,
    double Confidence,
    string Method,
    string Evidence,
    bool Persisted);

/// <summary>
/// Detects embedded subtitle streams whose content language contradicts the container tag,
/// persists the verdict on the subtitle row (never rewriting the tag) and exposes the
/// effective language used by source selection.
/// </summary>
public interface ISubtitleLanguageContradictionDetector
{
    /// <summary>Algorithm version stored with every verdict to invalidate stale results.</summary>
    int CurrentVersion { get; }

    /// <summary>True when the stored verdict for the subtitle is still within its cache window.</summary>
    bool IsCheckFresh(EmbeddedSubtitle subtitle, DateTime? now = null);

    /// <summary>Checks one subtitle stream and persists the outcome.</summary>
    Task<SubtitleLanguageCheckOutcome> CheckAsync(
        SubtitleLanguageCheckRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks several streams. Deterministic analysis runs for every stream; ambiguous
    /// Latin-script samples are resolved with at most one batched LLM call.
    /// </summary>
    Task<IReadOnlyList<SubtitleLanguageCheckOutcome>> CheckBatchAsync(
        IReadOnlyList<SubtitleLanguageCheckRequest> requests,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Failure-driven shortcut. When a request cannot find a source in its declared
    /// language, checks the readable embedded streams and adopts a high confidence
    /// detected language by rewriting the request source language (not the stream tag).
    /// Returns the stream that was adopted, or null when nothing changed.
    /// </summary>
    Task<EmbeddedSubtitle?> TryAdoptDetectedSourceLanguageAsync(
        TranslationRequest request,
        IReadOnlyList<EmbeddedSubtitle> embeddedSubtitles,
        string mediaFilePath,
        CancellationToken cancellationToken = default);
}
