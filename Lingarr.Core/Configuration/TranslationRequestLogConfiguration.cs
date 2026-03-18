using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Lingarr.Core.Entities;

namespace Lingarr.Core.Configuration;

public class TranslationRequestLogConfiguration : IEntityTypeConfiguration<TranslationRequestLog>
{
    public void Configure(EntityTypeBuilder<TranslationRequestLog> builder)
    {
        // Index for foreign key lookups (joining with TranslationRequest)
        builder.HasIndex(trl => trl.TranslationRequestId)
            .HasDatabaseName("IX_TranslationRequestLog_TranslationRequestId");

        // Index for log level filtering
        builder.HasIndex(trl => trl.Level)
            .HasDatabaseName("IX_TranslationRequestLog_Level");
    }
}