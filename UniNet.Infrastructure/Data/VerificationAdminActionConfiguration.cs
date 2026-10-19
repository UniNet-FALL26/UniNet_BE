using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UniNet.Domain.Entities;
using UniNet.Domain;
using UniNet.Domain.Enums;

namespace UniNet.Infrastructure.Data;

public sealed class VerificationAdminActionConfiguration : IEntityTypeConfiguration<VerificationAdminAction>
{
    public void Configure(EntityTypeBuilder<VerificationAdminAction> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Decision).HasMaxLength(100).IsRequired();
        e.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
        e.HasOne<ProjectVerification>().WithMany().HasForeignKey(x => x.VerificationId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<RefundTransaction>().WithMany().HasForeignKey(x => x.RefundId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.VerificationId, x.CreatedAt });
    }
}

public sealed class VerificationNotificationConfiguration : IEntityTypeConfiguration<VerificationNotification>
{
    public void Configure(EntityTypeBuilder<VerificationNotification> e)
    {
        e.HasKey(n => n.Id);
        e.Property(n => n.EventKey).HasMaxLength(200).IsRequired();
        e.Property(n => n.Message).HasMaxLength(2000).IsRequired();
        e.HasIndex(n => new { n.EventKey, n.RecipientUserId }).IsUnique();
        e.HasIndex(n => new { n.RecipientUserId, n.CreatedAt });
        e.HasOne<ProjectVerification>().WithMany().HasForeignKey(n => n.VerificationId).OnDelete(DeleteBehavior.Restrict);
        e.Property<ProjectModerationPurpose>("EvidenceModerationPurpose").HasConversion<short>().HasDefaultValue(ProjectModerationPurpose.ProjectEvidence);
        e.HasOne<ProjectModeration>().WithMany().HasForeignKey("EvidenceModerationId", "EvidenceModerationPurpose")
            .HasPrincipalKey(x => new { x.Id, x.Purpose }).OnDelete(DeleteBehavior.Restrict);
        e.ToTable("VerificationNotification", t => t.HasCheckConstraint("CK_VerificationNotification_EvidencePurpose", "\"EvidenceModerationPurpose\" = 1"));
        e.HasOne<UserVerification>().WithMany().HasForeignKey(n => n.UserVerificationId).OnDelete(DeleteBehavior.Restrict);
        e.ToTable("VerificationNotification", t => t.HasCheckConstraint("CK_VerificationNotification_Target",
            "(CASE WHEN \"VerificationId\" IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN \"EvidenceModerationId\" IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN \"UserVerificationId\" IS NOT NULL THEN 1 ELSE 0 END) = 1"));
        e.HasOne<UserProfile>().WithMany().HasForeignKey(n => n.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
