using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Lingarr.Core.Entities;

namespace Lingarr.Core.Configuration;

public class EpisodeConfiguration : IEntityTypeConfiguration<Episode>
{
    public void Configure(EntityTypeBuilder<Episode> builder)
    {
        builder
            .HasOne(e => e.Season)
            .WithMany(s => s.Episodes)
            .HasForeignKey(e => e.SeasonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.TranslationState)
            .HasDatabaseName("IX_Episodes_TranslationState");

        builder.HasIndex(e => new { e.SourceInstanceId, e.SonarrId })
            .IsUnique()
            .HasDatabaseName("IX_Episodes_SourceInstanceId_SonarrId");
    }
}