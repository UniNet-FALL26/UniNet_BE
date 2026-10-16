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
