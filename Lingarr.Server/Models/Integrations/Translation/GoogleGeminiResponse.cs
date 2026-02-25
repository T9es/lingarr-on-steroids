using System.Text.Json.Serialization;

namespace Lingarr.Server.Models.Integrations.Translation;

public class GeminiResponse
{
    [JsonPropertyName("candidates")] 
    public List<Candidate> Candidates { get; set; } = new();

    [JsonPropertyName("usageMetadata")]
    public UsageMetadataInfo? UsageMetadata { get; set; }

    public class UsageMetadataInfo
    {
        [JsonPropertyName("promptTokenCount")]
        public int PromptTokenCount { get; set; }

        [JsonPropertyName("candidatesTokenCount")]
        public int CandidatesTokenCount { get; set; }

        [JsonPropertyName("totalTokenCount")]
        public int TotalTokenCount { get; set; }
    }

    public class Candidate
    {
        [JsonPropertyName("content")] 
        public Content? Content { get; set; }
    }

    public class Content
    {
        [JsonPropertyName("parts")] 
        public List<Part> Parts { get; set; } = new();
    }

    public class Part
    {
        [JsonPropertyName("text")] 
        public string Text { get; set; } = string.Empty;
    }
}