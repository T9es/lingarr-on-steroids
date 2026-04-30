using Lingarr.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lingarr.Core.Configuration;

public class TranslationDiagnosticEventConfiguration : IEntityTypeConfiguration<TranslationDiagnosticEvent>
{
    public void Configure(EntityTypeBuilder<TranslationDiagnosticEvent> builder)
    {
        builder.HasIndex(e => e.TranslationRequestId)
            .HasDatabaseName("IX_TranslationDiagnosticEvents_TranslationRequestId");

        builder.HasIndex(e => new { e.MediaType, e.MediaId })
            .HasDatabaseName("IX_TranslationDiagnosticEvents_Media");

        builder.HasIndex(e => e.ExpiresAt)
            .HasDatabaseName("IX_TranslationDiagnosticEvents_ExpiresAt");

        builder.HasIndex(e => e.ReasonCode)
            .HasDatabaseName("IX_TranslationDiagnosticEvents_ReasonCode");
    }
}
