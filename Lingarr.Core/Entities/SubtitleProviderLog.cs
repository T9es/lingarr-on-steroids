using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lingarr.Core.Entities;

public class SubtitleProviderLog : BaseEntity
{
    public int? MediaId { get; set; }
    public string MediaType { get; set; } = string.Empty; // "Movie" or "Episode"
    public required string ProviderName { get; set; }
    public required string Message { get; set; }
    public string Level { get; set; } = "Info"; // Info, Warning, Error
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    [Column(TypeName = "jsonb")]
    public string? Details { get; set; }
}
