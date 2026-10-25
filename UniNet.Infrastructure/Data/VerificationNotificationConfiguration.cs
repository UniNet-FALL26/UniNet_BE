using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UniNet.Domain.Entities;
using UniNet.Domain;

namespace UniNet.Infrastructure.Data;

public sealed class VerificationNotificationConfiguration : IEntityTypeConfiguration<VerificationNotification>
{
    public void Configure(EntityTypeBuilder<VerificationNotification> e)
    {
        e.ToTable("Notifications"); e.HasKey(n => n.Id);
        e.Property(n => n.Type).HasMaxLength(60).IsRequired();
        e.Property(n => n.Category).HasMaxLength(40).IsRequired();
        e.Property(n => n.Title).HasMaxLength(200).IsRequired();
        e.Property(n => n.Message).IsRequired();
        e.Property(n => n.Audience).HasMaxLength(30).IsRequired();
        e.Property(n => n.RequiredPermission).HasMaxLength(100);
        e.Property(n => n.TargetType).HasMaxLength(60);
        e.Property(n => n.TargetUrl).HasMaxLength(500);
        e.Property(n => n.EventKey).HasMaxLength(200);
        e.HasIndex(n => new { n.RecipientUserId, n.EventKey }).IsUnique().HasFilter("\"EventKey\" IS NOT NULL");
        e.HasIndex(n => new { n.RecipientUserId, n.CreatedAt });
        e.HasIndex(n => new { n.RecipientUserId, n.ReadAt, n.CreatedAt });
        e.HasIndex(n => n.BroadcastId);
        e.HasOne<Account>().WithMany().HasForeignKey(n => n.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Account>().WithMany().HasForeignKey(n => n.SenderUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
