namespace Lingarr.Server.Models.Api;

public class TranslationRequestLogDto
{
    public int Id { get; set; }
    public required string Level { get; set; }
    public required string Message { get; set; }
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; }
}

