using Lingarr.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lingarr.Core.Configuration;

public class UploadBatchConfiguration : IEntityTypeConfiguration<UploadBatch>
{
    public void Configure(EntityTypeBuilder<UploadBatch> builder)
    {
        builder.Property(batch => batch.Name)
            .HasMaxLength(200);

        builder.Property(batch => batch.TargetLanguage)
            .HasMaxLength(32);

        builder.Property(batch => batch.StoragePath)
            .HasMaxLength(2048);

        builder.Property(batch => batch.FailureReason)
            .HasMaxLength(4096);

        builder.HasIndex(batch => batch.Status)
            .HasDatabaseName("IX_UploadBatches_Status");

        builder.HasIndex(batch => batch.ExpiresAt)
            .HasDatabaseName("IX_UploadBatches_ExpiresAt");
    }
}
