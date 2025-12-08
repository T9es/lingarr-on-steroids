using Lingarr.Core.Enum;

namespace Lingarr.Server.Models.Api;

/// <summary>
/// Request model for manually triggering translation of a media item.
/// </summary>
public class TranslateMediaRequest
{
    /// <summary>
    /// The ID of the media item to translate.
    /// </summary>
    public int MediaId { get; set; }
    
    /// <summary>
    /// The type of media (Movie, Episode, Season, Show).
    /// </summary>
    public MediaType MediaType { get; set; }
}
