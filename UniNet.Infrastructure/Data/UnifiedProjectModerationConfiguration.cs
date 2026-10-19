using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;

namespace UniNet.Infrastructure.Data;

public sealed class UnifiedProjectModerationConfiguration : IEntityTypeConfiguration<ProjectModeration>
{
    public void Configure(EntityTypeBuilder<ProjectModeration> e)
    {
        e.Property(x => x.Purpose).HasConversion<short>();
        e.Property(x => x.Purpose).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        e.HasAlternateKey(x => new { x.Id, x.Purpose });
        e.Property(x => x.SourceHash).HasMaxLength(64);
        e.Property(x => x.PolicyHash).HasMaxLength(64);
        e.Property(x => x.SnapshotJson).HasColumnType("jsonb");
        e.Property(x => x.PolicyJson).HasColumnType("jsonb");
        e.Property(x => x.LeaseToken).IsConcurrencyToken();
        e.Property(x => x.SupersededAt).IsConcurrencyToken();
        e.HasIndex(x => new { x.ProjectId, x.Version }).IsUnique().HasFilter("\"Purpose\" = 1");
        e.HasIndex(x => x.ProjectId).IsUnique().HasFilter("\"Purpose\" = 1 AND \"SupersededAt\" IS NULL");
        e.HasIndex(x => new { x.Purpose, x.Status, x.NextRetryAt });
        e.ToTable("ProjectModerations", t =>
        {
            t.HasCheckConstraint("CK_ProjectModerations_Purpose", "\"Purpose\" IN (0,1)");
            t.HasCheckConstraint("CK_ProjectModerations_EvidenceSnapshot", "\"Purpose\" = 0 OR (\"Version\" > 0 AND \"SourceHash\" IS NOT NULL AND \"SnapshotJson\" IS NOT NULL AND \"PolicyHash\" IS NOT NULL AND \"PolicyJson\" IS NOT NULL)");
        });
    }
}

public sealed class EvidenceModerationAttemptConfiguration : IEntityTypeConfiguration<EvidenceModerationAttempt>
{
    public void Configure(EntityTypeBuilder<EvidenceModerationAttempt> e)
    {
        e.ToTable("EvidenceModerationAttempts", t => t.HasCheckConstraint("CK_EvidenceModerationAttempts_Purpose", "\"ModerationPurpose\" = 1"));
        e.HasKey(x => x.Id);
        e.Property<ProjectModerationPurpose>("ModerationPurpose").HasConversion<short>().HasDefaultValue(ProjectModerationPurpose.ProjectEvidence);
        e.HasOne<ProjectModeration>().WithMany(x => x.Attempts)
            .HasForeignKey("ModerationId", "ModerationPurpose").HasPrincipalKey(x => new { x.Id, x.Purpose }).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.ModerationId, x.Number }).IsUnique();
        e.Property(x => x.ResultsJson).HasColumnType("jsonb");
        foreach (var name in new[] { nameof(EvidenceModerationAttempt.ModerationId), nameof(EvidenceModerationAttempt.Number), nameof(EvidenceModerationAttempt.StartedAt), "ModerationPurpose" })
            e.Property(name).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
