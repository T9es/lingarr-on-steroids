using Lingarr.Core.Enum;

namespace Lingarr.Core.Entities;

public class CustomSource : BaseEntity
{
    public required string Name { get; set; }
    public required CustomSourceType SourceType { get; set; }
    public required string RootPath { get; set; }
    public bool Recursive { get; set; }
    public bool Enabled { get; set; }
    public bool IncludeInAutomation { get; set; }
    public DateTime? LastScannedAt { get; set; }
    public string? LastScanResult { get; set; }
    public string? LastScanError { get; set; }
    public List<CustomMediaItem> Items { get; set; } = new();
}
