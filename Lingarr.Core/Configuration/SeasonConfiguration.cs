using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Lingarr.Core.Entities;

namespace Lingarr.Core.Configuration;

public class SeasonConfiguration : IEntityTypeConfiguration<Season>
{
    public void Configure(EntityTypeBuilder<Season> builder)
    {
        builder
            .HasMany(s => s.Episodes)
            .WithOne(e => e.Season)
            .HasForeignKey(e => e.SeasonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(s => s.Episodes).AutoInclude();

        builder
            .HasOne(s => s.Show)
            .WithMany(show => show.Seasons)
            .HasForeignKey(s => s.ShowId)
            .OnDelete(DeleteBehavior.Cascade);

        // Index for foreign key lookups to Show
        builder.HasIndex(s => s.ShowId)
            .HasDatabaseName("IX_Seasons_ShowId");

        // Index for season number queries
        builder.HasIndex(s => s.SeasonNumber)
            .HasDatabaseName("IX_Seasons_SeasonNumber");
    }
}
