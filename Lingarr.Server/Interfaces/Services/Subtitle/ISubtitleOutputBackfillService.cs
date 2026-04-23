using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;

namespace Lingarr.Server.Interfaces.Services.Subtitle;

public interface ISubtitleOutputBackfillService
{
    Task<SubtitleOutputBackfillResult> BackfillMissingOutputsAsync(
        IMedia media,
        MediaType mediaType,
        IReadOnlyCollection<Subtitles> matchingSubtitles,
        SubtitleOutputMode subtitleOutputMode,
        string subtitleTag,
        string subtitleTagShort,
        CancellationToken cancellationToken = default);

    Task<SubtitleOutputBackfillResult> RepairExistingAssOutputsAsync(
        IMedia media,
        MediaType mediaType,
        Lingarr.Core.Entities.TranslationRequest request,
        IReadOnlyCollection<Subtitles> matchingSubtitles,
        string subtitleTag,
        string subtitleTagShort,
        CancellationToken cancellationToken = default);
}
