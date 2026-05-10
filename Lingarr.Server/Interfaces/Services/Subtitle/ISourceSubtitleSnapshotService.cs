using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Subtitle;

namespace Lingarr.Server.Interfaces.Services.Subtitle;

public interface ISourceSubtitleSnapshotService
{
    Task<ResolvedExternalSourceSubtitle?> ResolveExternalSourceAsync(
        IMedia media,
        IReadOnlyCollection<Subtitles>? externalSubtitles = null,
        CancellationToken cancellationToken = default);

    Task<SourceSubtitleSnapshot?> ResolveCurrentSnapshotAsync(
        IMedia media,
        MediaType mediaType,
        IReadOnlyCollection<EmbeddedSubtitle> embeddedSubtitles,
        IReadOnlyCollection<Subtitles>? externalSubtitles = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the best available external subtitle, optionally using auto mode
    /// to bypass configured source language filtering.
    /// </summary>
    Task<ResolvedExternalSourceSubtitle?> ResolveExternalSourceWithAutoAsync(
        IMedia media,
        IReadOnlyCollection<Subtitles>? externalSubtitles,
        bool useAutoMode,
        IReadOnlyList<string>? targetLanguages,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a snapshot of the current source subtitle, optionally using auto mode
    /// to bypass configured source language filtering.
    /// </summary>
    Task<SourceSubtitleSnapshot?> ResolveCurrentSnapshotWithAutoAsync(
        IMedia media,
        MediaType mediaType,
        IReadOnlyCollection<EmbeddedSubtitle> embeddedSubtitles,
        IReadOnlyCollection<Subtitles>? externalSubtitles,
        bool useAutoMode,
        IReadOnlyList<string>? targetLanguages,
        CancellationToken cancellationToken = default);

    SourceSubtitleSnapshot CreateExternalSnapshot(string subtitlePath, string sourceLanguage);

    SourceSubtitleSnapshot CreateEmbeddedSnapshot(EmbeddedSubtitle subtitle, string sourceLanguage);

    bool IsRequestStaleForSnapshot(TranslationRequest request, SourceSubtitleSnapshot currentSnapshot);

    Task<HashSet<string>> GetStaleTargetLanguagesAsync(
        int mediaId,
        MediaType mediaType,
        IEnumerable<string> targetLanguages,
        SourceSubtitleSnapshot? currentSnapshot,
        CancellationToken cancellationToken = default);
}
