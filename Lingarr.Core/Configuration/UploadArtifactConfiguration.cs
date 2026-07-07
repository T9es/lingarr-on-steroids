using Lingarr.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lingarr.Core.Configuration;

public class UploadArtifactConfiguration : IEntityTypeConfiguration<UploadArtifact>
{
    public void Configure(EntityTypeBuilder<UploadArtifact> builder)
    {
        builder.Property(artifact => artifact.FileName)
            .HasMaxLength(260);

        builder.Property(artifact => artifact.Path)
            .HasMaxLength(2048);

        builder.Property(artifact => artifact.RelativePath)
            .HasMaxLength(2048);

        builder.Property(artifact => artifact.ContentType)
            .HasMaxLength(128);

        builder.HasOne(artifact => artifact.UploadBatch)
            .WithMany(batch => batch.Artifacts)
            .HasForeignKey(artifact => artifact.UploadBatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(artifact => artifact.UploadBatchFile)
            .WithMany(file => file.Artifacts)
            .HasForeignKey(artifact => artifact.UploadBatchFileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(artifact => artifact.UploadBatchId)
            .HasDatabaseName("IX_UploadArtifacts_UploadBatchId");

        builder.HasIndex(artifact => artifact.UploadBatchFileId)
            .HasDatabaseName("IX_UploadArtifacts_UploadBatchFileId");

        builder.HasIndex(artifact => new { artifact.UploadBatchFileId, artifact.Kind })
            .HasDatabaseName("IX_UploadArtifacts_UploadBatchFileId_Kind");
    }
}
