using System.ComponentModel.DataAnnotations.Schema;

namespace Lingarr.Core.Entities;

public class TranslationRequestLog : BaseEntity
{
    public int TranslationRequestId { get; set; }

    [ForeignKey(nameof(TranslationRequestId))]
    public TranslationRequest? TranslationRequest { get; set; }

    public required string Level { get; set; }
    public required string Message { get; set; }
    public string? Details { get; set; }
}

