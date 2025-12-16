using System.Text.Json.Serialization;

namespace Lingarr.Core.Models;

public class GitHubReleaseInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; } = string.Empty;

    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; } = string.Empty;
}