using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;

namespace UniNet.Infrastructure.Data;

public static class NotificationConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<NotificationDelivery>(e => {
            e.ToTable("NotificationDeliveries"); e.HasKey(x => x.Id);
            e.Property(x => x.Reason).HasMaxLength(30);
            e.HasIndex(x => new { x.DeliveredAt, x.CreatedAt });
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<NotificationPermissionOverride>(e => {
            e.ToTable("NotificationPermissionOverrides"); e.HasKey(x => new { x.AccountId, x.Permission });
            e.Property(x => x.Permission).HasMaxLength(100);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<NotificationBroadcast>(e => {
            e.ToTable("NotificationBroadcasts"); e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200); e.Property(x => x.Audience).HasMaxLength(30);
            e.Property(x => x.Role).HasMaxLength(30); e.Property(x => x.Status).HasMaxLength(30).IsConcurrencyToken();
            e.Property(x => x.TargetUrl).HasMaxLength(500); e.Property(x => x.LastError).HasMaxLength(500);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.SenderUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Recipients).WithOne().HasForeignKey(x => x.BroadcastId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<NotificationBroadcastRecipient>(e => {
            e.ToTable("NotificationBroadcastRecipients"); e.HasKey(x => new { x.BroadcastId, x.AccountId });
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
