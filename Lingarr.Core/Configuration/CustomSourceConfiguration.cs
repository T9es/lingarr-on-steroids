using Lingarr.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lingarr.Core.Configuration;

public class CustomSourceConfiguration : IEntityTypeConfiguration<CustomSource>
{
    public void Configure(EntityTypeBuilder<CustomSource> builder)
    {
        builder.Property(source => source.Name)
            .HasMaxLength(200);

        builder.Property(source => source.RootPath)
            .HasMaxLength(1024);

        builder.HasIndex(source => source.Name)
            .IsUnique()
            .HasDatabaseName("IX_CustomSources_Name");

        builder.HasIndex(source => source.RootPath)
            .HasDatabaseName("IX_CustomSources_RootPath");
    }
}
