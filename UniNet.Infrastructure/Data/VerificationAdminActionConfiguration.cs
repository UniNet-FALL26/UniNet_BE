using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UniNet.Domain.Entities;
using UniNet.Domain;

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
        e.HasOne<UserProfile>().WithMany().HasForeignKey(n => n.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
