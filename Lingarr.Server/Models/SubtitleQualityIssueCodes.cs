namespace Lingarr.Server.Models;

public static class SubtitleQualityIssueCodes
{
    public const string MissingSource = "missing_source";
    public const string MissingTarget = "missing_target";
    public const string EmptySource = "empty_source";
    public const string TooShort = "too_short";
    public const string TooLong = "too_long";
    public const string UnchangedSourceText = "unchanged_source_text";
    public const string TargetLanguageMismatch = "target_language_mismatch";
    public const string DrawingArtifact = "drawing_artifact";
    public const string UnexpectedAssTags = "unexpected_ass_tags";
    public const string AssTagMismatch = "ass_tag_mismatch";
    public const string InlineAssTagPlacement = "inline_ass_tag_placement";
    public const string CacheOnlyOutput = "cache_only_output";
    public const string StaleSourceSnapshot = "stale_source_snapshot";
    public const string ValidationError = "validation_error";
}
