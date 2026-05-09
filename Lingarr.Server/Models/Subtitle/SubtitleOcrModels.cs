using Lingarr.Core.Enum;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Models.Subtitle;

public sealed class SubtitleOcrEngineResult
{
    public bool Success { get; init; }
    public string? OutputPath { get; init; }
    public string? Error { get; init; }
}

public sealed class SubtitleOcrResult
{
    public bool Success { get; init; }
    public SubtitleOcrStatus Status { get; init; }
    public string? ExtractedPath { get; init; }
    public string? Error { get; init; }
    public int? CueCount { get; init; }
    public int? QualityScore { get; init; }
    public string? IssueSummary { get; init; }
}

public sealed class SubtitleOcrQualityResult
{
    public bool Accepted { get; init; }
    public int CueCount { get; init; }
    public int QualityScore { get; init; }
    public string IssueSummary { get; init; } = string.Empty;
    public IReadOnlyList<SubtitleItem> Subtitles { get; init; } = [];
}

public sealed class SubtitleOcrPreviewResponse
{
    public bool Success { get; init; }
    public SubtitleOcrStatus Status { get; init; }
    public int? CueCount { get; init; }
    public int? QualityScore { get; init; }
    public string? IssueSummary { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<SubtitleOcrPreviewLine> Lines { get; init; } = [];
}

public sealed class SubtitleOcrPreviewLine
{
    public int Position { get; init; }
    public int StartTime { get; init; }
    public int EndTime { get; init; }
    public string Text { get; init; } = string.Empty;
}
