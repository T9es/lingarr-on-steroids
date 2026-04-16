using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Models.Subtitle;

public class ResolvedExternalSourceSubtitle
{
    public required Subtitles Subtitle { get; init; }

    public required string SourceLanguage { get; init; }

    public required SourceSubtitleSnapshot Snapshot { get; init; }
}
