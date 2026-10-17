namespace Lingarr.Server.Models.Subtitle;

/// <summary>
/// Controls source selection for a translation execution.
/// </summary>
public sealed record TranslationSourcePreparationOptions(
    bool HasManualStreamSelection,
    bool AllowCaptionFallback,
    bool IsSupplementalRequest,
    bool SupplementalTranslationEnabled);
