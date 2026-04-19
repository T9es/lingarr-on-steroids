using Lingarr.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lingarr.Core.Configuration;

public class CustomMediaItemConfiguration : IEntityTypeConfiguration<CustomMediaItem>
{
    public void Configure(EntityTypeBuilder<CustomMediaItem> builder)
    {
        builder.HasOne(item => item.CustomSource)
            .WithMany(source => source.Items)
            .HasForeignKey(item => item.CustomSourceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(item => item.CustomSourceId)
            .HasDatabaseName("IX_CustomMediaItems_CustomSourceId");

        builder.HasIndex(item => new { item.CustomSourceId, item.Path })
            .IsUnique()
            .HasDatabaseName("IX_CustomMediaItems_CustomSourceId_Path");

        builder.HasIndex(item => item.TranslationState)
            .HasDatabaseName("IX_CustomMediaItems_TranslationState");
    }
}
