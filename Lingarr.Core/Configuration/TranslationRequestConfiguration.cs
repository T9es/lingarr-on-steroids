using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Lingarr.Core.Entities;

namespace Lingarr.Core.Configuration;

public class TranslationRequestConfiguration : IEntityTypeConfiguration<TranslationRequest>
{
    public void Configure(EntityTypeBuilder<TranslationRequest> builder)
    {
        // Index for priority queue ordering (OrderByDescending IsPriority)
        builder.HasIndex(tr => tr.IsPriority)
            .HasDatabaseName("IX_TranslationRequests_IsPriority");

        // Index for retry queries (filtering by NextRetryAt, FailedAt)
        builder.HasIndex(tr => tr.NextRetryAt)
            .HasDatabaseName("IX_TranslationRequests_NextRetryAt");

        builder.HasIndex(tr => tr.FailedAt)
            .HasDatabaseName("IX_TranslationRequests_FailedAt");

        // Index for status-based queries
        builder.HasIndex(tr => tr.Status)
            .HasDatabaseName("IX_TranslationRequests_Status");

        // Composite index for common query pattern: pending + priority + created
        builder.HasIndex(tr => new { tr.Status, tr.IsPriority, tr.CreatedAt })
            .HasDatabaseName("IX_TranslationRequests_Status_Priority_Created");

        // Composite index for stale source freshness lookups by media + target language.
        builder.HasIndex(tr => new { tr.MediaId, tr.MediaType, tr.TargetLanguage, tr.RequiredOutputFormats, tr.Status, tr.CompletedAt })
            .HasDatabaseName("IX_TranslationRequests_FreshnessLookup");

        builder.Property(tr => tr.SourceSnapshotVersion)
            .HasDefaultValue(1);
    }
}
