using Lingarr.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lingarr.Core.Configuration;

public class TranslationFailedCueConfiguration : IEntityTypeConfiguration<TranslationFailedCue>
{
    /// <summary>
    /// Maximum persisted length of the rejected provider translation. Writers must truncate
    /// to this length so an oversized provider response cannot fail the insert/update.
    /// </summary>
    public const int RejectedTextMaxLength = 4000;

    /// <summary>
    /// Maximum persisted length of the rejection reason code. Writers must truncate to this
    /// length so unexpected input cannot fail the insert/update.
    /// </summary>
    public const int RejectionReasonMaxLength = 100;

    public void Configure(EntityTypeBuilder<TranslationFailedCue> builder)
    {
        builder.HasIndex(cue => new { cue.TranslationRequestId, cue.Position })
            .IsUnique()
            .HasDatabaseName("ux_translation_failed_cues_request_position");

        builder.HasIndex(cue => cue.TextHash)
            .HasDatabaseName("ix_translation_failed_cues_text_hash");

        builder.HasIndex(cue => new { cue.AutoApprovalEligible, cue.AutoApprovedAt })
            .HasDatabaseName("ix_translation_failed_cues_auto_approval");

        builder.Property(cue => cue.SourceText)
            .HasMaxLength(4000);

        builder.Property(cue => cue.NormalizedText)
            .HasMaxLength(1000);

        builder.Property(cue => cue.TextHash)
            .HasMaxLength(64);

        builder.Property(cue => cue.ApprovalSequenceHash)
            .HasMaxLength(64);

        builder.Property(cue => cue.RejectedText)
            .HasMaxLength(RejectedTextMaxLength);

        builder.Property(cue => cue.RejectionReason)
            .HasMaxLength(RejectionReasonMaxLength);

        builder.HasOne(cue => cue.TranslationRequest)
            .WithMany()
            .HasForeignKey(cue => cue.TranslationRequestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
