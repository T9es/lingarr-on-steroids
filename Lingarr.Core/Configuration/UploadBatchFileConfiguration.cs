using Lingarr.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lingarr.Core.Configuration;

public class UploadBatchFileConfiguration : IEntityTypeConfiguration<UploadBatchFile>
{
    public void Configure(EntityTypeBuilder<UploadBatchFile> builder)
    {
        builder.Property(file => file.Title)
            .HasMaxLength(300);

        builder.Property(file => file.OriginalFileName)
            .HasMaxLength(260);

        builder.Property(file => file.StoredPath)
            .HasMaxLength(2048);

        builder.Property(file => file.RelativeStoredPath)
            .HasMaxLength(2048);

        builder.Property(file => file.DetectedSourceLanguage)
            .HasMaxLength(32);

        builder.Property(file => file.SelectedSourceLanguage)
            .HasMaxLength(32);

        builder.Property(file => file.SelectedEmbeddedStreamLanguage)
            .HasMaxLength(32);

        builder.Property(file => file.SelectedEmbeddedStreamTitle)
            .HasMaxLength(512);

        builder.Property(file => file.SelectedEmbeddedStreamCodec)
            .HasMaxLength(64);

        builder.Property(file => file.ProbeError)
            .HasMaxLength(4096);

        builder.Property(file => file.LastError)
            .HasMaxLength(4096);

        builder.HasOne(file => file.UploadBatch)
            .WithMany(batch => batch.Files)
            .HasForeignKey(file => file.UploadBatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(file => file.UploadBatchId)
            .HasDatabaseName("IX_UploadBatchFiles_UploadBatchId");

        builder.HasIndex(file => file.Status)
            .HasDatabaseName("IX_UploadBatchFiles_Status");

        builder.HasIndex(file => new { file.UploadBatchId, file.OriginalFileName })
            .HasDatabaseName("IX_UploadBatchFiles_UploadBatchId_OriginalFileName");
    }
}
