using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UniNet.Domain.Entities;

namespace UniNet.Infrastructure.Data;

public sealed class ProjectEvidenceModerationConfiguration : IEntityTypeConfiguration<ProjectEvidenceModeration>
{
    public void Configure(EntityTypeBuilder<ProjectEvidenceModeration> e)
    {
        e.ToTable("ProjectEvidenceModerations", t =>
        {
            t.HasCheckConstraint("CK_EvidenceModeration_Status", "\"Status\" BETWEEN 0 AND 5");
            t.HasCheckConstraint("CK_EvidenceModeration_Version", "\"Version\" > 0");
        });
        e.HasKey(x => x.Id);
        e.Property(x => x.Status).HasConversion<short>();
        e.Property(x => x.LeaseToken).IsConcurrencyToken();
        e.Property(x => x.SupersededAt).IsConcurrencyToken();
        e.Property(x => x.SourceHash).HasMaxLength(64).IsRequired();
        e.Property(x => x.PolicyHash).HasMaxLength(64).IsRequired();
        e.Property(x => x.SnapshotJson).HasColumnType("jsonb").IsRequired();
        e.Property(x => x.PolicyJson).HasColumnType("jsonb").IsRequired();
        e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Attempts).WithOne().HasForeignKey(x => x.ModerationId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.ProjectId, x.Version }).IsUnique();
        e.HasIndex(x => x.ProjectId).IsUnique().HasFilter("\"SupersededAt\" IS NULL");
        e.HasIndex(x => new { x.Status, x.NextRetryAt });
        foreach (var name in new[] { nameof(ProjectEvidenceModeration.ProjectId), nameof(ProjectEvidenceModeration.Version),
            nameof(ProjectEvidenceModeration.SourceHash), nameof(ProjectEvidenceModeration.SnapshotJson),
            nameof(ProjectEvidenceModeration.PolicyHash), nameof(ProjectEvidenceModeration.PolicyJson), nameof(ProjectEvidenceModeration.CreatedAt) })
            e.Property(name).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class EvidenceModerationAttemptConfiguration : IEntityTypeConfiguration<EvidenceModerationAttempt>
{
    public void Configure(EntityTypeBuilder<EvidenceModerationAttempt> e)
    {
        e.ToTable("EvidenceModerationAttempts"); e.HasKey(x => x.Id);
        e.HasIndex(x => new { x.ModerationId, x.Number }).IsUnique();
        e.Property(x => x.ResultsJson).HasColumnType("jsonb");
        foreach (var name in new[] { nameof(EvidenceModerationAttempt.ModerationId), nameof(EvidenceModerationAttempt.Number), nameof(EvidenceModerationAttempt.StartedAt) })
            e.Property(name).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
