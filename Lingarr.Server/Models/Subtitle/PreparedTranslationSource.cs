using Lingarr.Core.Entities;

namespace Lingarr.Server.Models.Subtitle;

/// <summary>
/// Describes the validated source subtitle selected for one translation execution.
/// </summary>
public sealed record PreparedTranslationSource(
    string Path,
    EmbeddedSubtitle? SelectedSubtitle,
    int EntryCount,
    bool IsManualSelection);
