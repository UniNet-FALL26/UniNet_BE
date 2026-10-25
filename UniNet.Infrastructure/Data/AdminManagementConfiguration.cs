using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;

namespace UniNet.Infrastructure.Data;

public static class AdminManagementConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<Account>().Property(a => a.SessionVersion).HasDefaultValue(0);
        model.Entity<AccountStatusChange>(e => {
            e.ToTable("AccountStatusChanges"); e.HasKey(x => x.Id);
            e.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
            e.Property(x => x.PreviousStatus).HasConversion<short>(); e.Property(x => x.Status).HasConversion<short>();
            e.HasIndex(x => new { x.AccountId, x.CreatedAt });
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.ActorAccountId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
