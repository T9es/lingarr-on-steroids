using System.ComponentModel.DataAnnotations;

namespace Lingarr.Server.Models.Batch.Request;

/// <summary>
/// Subtitle translation request model
/// </summary>
public class BatchTranslationRequest
{
    /// <summary>
    /// Source language code
    /// </summary>
    [Required]
    public required string SourceLanguage { get; set; }

    /// <summary>
    /// Target language code
    /// </summary>
    [Required]
    public required string TargetLanguage { get; set; }

    /// <summary>
    /// Subtitle Lines to translate
    /// </summary>
    [Required]
    public required List<BatchSubtitleLine> Lines { get; set; }
}