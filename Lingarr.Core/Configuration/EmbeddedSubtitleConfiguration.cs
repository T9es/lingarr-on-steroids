using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Lingarr.Core.Entities;

namespace Lingarr.Core.Configuration;

public class EmbeddedSubtitleConfiguration : IEntityTypeConfiguration<EmbeddedSubtitle>
{
    public void Configure(EntityTypeBuilder<EmbeddedSubtitle> builder)
    {
        // Index for foreign key lookups to Movie
        builder.HasIndex(es => es.MovieId)
            .HasDatabaseName("IX_EmbeddedSubtitles_MovieId");

        // Index for foreign key lookups to Episode
        builder.HasIndex(es => es.EpisodeId)
            .HasDatabaseName("IX_EmbeddedSubtitles_EpisodeId");

        // Index for language filtering (common query pattern)
        builder.HasIndex(es => es.Language)
            .HasDatabaseName("IX_EmbeddedSubtitles_Language");

        // Index for extraction status filtering
        builder.HasIndex(es => es.IsExtracted)
            .HasDatabaseName("IX_EmbeddedSubtitles_IsExtracted");

        // Composite index for common query: find unextracted subtitles for a media item
        builder.HasIndex(es => new { es.MovieId, es.IsExtracted })
            .HasDatabaseName("IX_EmbeddedSubtitles_MovieId_IsExtracted");

        builder.HasIndex(es => new { es.EpisodeId, es.IsExtracted })
            .HasDatabaseName("IX_EmbeddedSubtitles_EpisodeId_IsExtracted");
    }
}