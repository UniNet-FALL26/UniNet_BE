using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;

namespace UniNet.Infrastructure.Data;

public static class FriendshipConfiguration
{
    public static void Configure(ModelBuilder model) => model.Entity<Friendship>(e => {
        e.ToTable("Friendships", t => {
            t.HasCheckConstraint("CK_Friendships_DifferentAccounts", "\"RequesterAccountId\" <> \"ReceiverAccountId\"");
            t.HasCheckConstraint("CK_Friendships_Status", "\"Status\" BETWEEN 0 AND 4");
        });
        e.HasKey(x => x.Id);
        e.Property(x => x.LowAccountId).HasComputedColumnSql("CASE WHEN \"RequesterAccountId\" < \"ReceiverAccountId\" THEN \"RequesterAccountId\" ELSE \"ReceiverAccountId\" END", stored: true);
        e.Property(x => x.HighAccountId).HasComputedColumnSql("CASE WHEN \"RequesterAccountId\" < \"ReceiverAccountId\" THEN \"ReceiverAccountId\" ELSE \"RequesterAccountId\" END", stored: true);
        e.HasIndex(x => new { x.LowAccountId, x.HighAccountId }).IsUnique();
        e.HasIndex(x => new { x.RequesterAccountId, x.Status, x.CreatedAt });
        e.HasIndex(x => new { x.ReceiverAccountId, x.Status, x.CreatedAt });
        e.Property(x => x.Version).IsConcurrencyToken();
        e.HasOne<Account>().WithMany().HasForeignKey(x => x.RequesterAccountId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Account>().WithMany().HasForeignKey(x => x.ReceiverAccountId).OnDelete(DeleteBehavior.Restrict);
    });
}
