using Lingarr.Core.Enum;
using Lingarr.Server.Models.Subtitle;

namespace Lingarr.Server.Models.Api;

public class SubtitleOcrResponse
{
    public bool Success { get; set; }
    public SubtitleOcrStatus Status { get; set; }
    public string? ExtractedPath { get; set; }
    public string? Error { get; set; }
    public int? CueCount { get; set; }
    public int? QualityScore { get; set; }
    public string? IssueSummary { get; set; }

    public static SubtitleOcrResponse FromResult(SubtitleOcrResult result) => new()
    {
        Success = result.Success,
        Status = result.Status,
        ExtractedPath = result.ExtractedPath,
        Error = result.Error,
        CueCount = result.CueCount,
        QualityScore = result.QualityScore,
        IssueSummary = result.IssueSummary
    };
}
