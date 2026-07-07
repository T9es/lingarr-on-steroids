using Lingarr.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lingarr.Core.Configuration;

public class UploadBatchFileSubtitleStreamConfiguration : IEntityTypeConfiguration<UploadBatchFileSubtitleStream>
{
    public void Configure(EntityTypeBuilder<UploadBatchFileSubtitleStream> builder)
    {
        builder.Property(stream => stream.Language)
            .HasMaxLength(32);

        builder.Property(stream => stream.Title)
            .HasMaxLength(512);

        builder.Property(stream => stream.CodecName)
            .HasMaxLength(64);

        builder.HasOne(stream => stream.UploadBatchFile)
            .WithMany(file => file.SubtitleStreams)
            .HasForeignKey(stream => stream.UploadBatchFileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(stream => stream.UploadBatchFileId)
            .HasDatabaseName("IX_UploadBatchFileSubtitleStreams_UploadBatchFileId");

        builder.HasIndex(stream => new { stream.UploadBatchFileId, stream.StreamIndex })
            .IsUnique()
            .HasDatabaseName("IX_UploadBatchFileSubtitleStreams_UploadBatchFileId_StreamIndex");
    }
}
