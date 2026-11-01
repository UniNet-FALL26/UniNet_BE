using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using UniNet.Domain;
using UniNet.Domain.Entities;

namespace UniNet.Infrastructure.Data;

internal static class FinanceConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<WalletEntry>(e =>
        {
            e.ToTable("WalletEntries", t => t.HasCheckConstraint("CK_WalletEntries_Amount", "\"Amount\" <> 0 AND ((\"Amount\" > 0 AND \"WithdrawalId\" IS NULL) OR (\"Amount\" < 0 AND \"WithdrawalId\" IS NOT NULL))"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(18, 0);
            e.Property(x => x.SourceReference).HasMaxLength(150);
            e.Property(x => x.Reason).HasMaxLength(2000);
            e.HasIndex(x => new { x.AccountId, x.SourceReference }).IsUnique();
            e.HasIndex(x => x.WithdrawalId).IsUnique();
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.ActorAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WalletWithdrawal>().WithMany().HasForeignKey(x => x.WithdrawalId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<WalletWithdrawal>(e =>
        {
            e.ToTable("WalletWithdrawals", t => {
                t.HasCheckConstraint("CK_WalletWithdrawals_Amount", "\"Amount\" > 0");
                t.HasCheckConstraint("CK_WalletWithdrawals_State", "\"State\" BETWEEN 0 AND 5");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(18, 0);
            e.Property(x => x.State).HasConversion<short>();
            e.Property(x => x.Version).IsConcurrencyToken();
            foreach (var name in new[] { nameof(WalletWithdrawal.BankName), nameof(WalletWithdrawal.BankAccountNumber), nameof(WalletWithdrawal.BankAccountHolder), nameof(WalletWithdrawal.PayingAccount), nameof(WalletWithdrawal.BankReference) })
                e.Property<string>(name).HasMaxLength(150);
            e.Property(x => x.ReviewReason).HasMaxLength(2000);
            e.Property(x => x.RejectionReason).HasMaxLength(2000);
            e.HasIndex(x => new { x.AccountId, x.RequestKey }).IsUnique();
            e.HasIndex(x => new { x.PayingAccount, x.BankReference }).IsUnique();
            e.HasIndex(x => new { x.State, x.CreatedAt });
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.ReviewedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.RejectedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.OrderedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.ConfirmedBy).OnDelete(DeleteBehavior.Restrict);
            foreach (var name in new[] { nameof(WalletWithdrawal.AccountId), nameof(WalletWithdrawal.RequestKey), nameof(WalletWithdrawal.Amount), nameof(WalletWithdrawal.BankName), nameof(WalletWithdrawal.BankAccountNumber), nameof(WalletWithdrawal.BankAccountHolder), nameof(WalletWithdrawal.CreatedAt) })
                e.Property(name).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        });
        model.Entity<RefundTransaction>(e => {
            e.Property(x => x.Approval).HasConversion<short>();
            e.Property(x => x.ReviewReason).HasMaxLength(2000);
            e.Property(x => x.ManualBankReference).HasMaxLength(150);
            e.Property(x => x.ManualPayingAccount).HasMaxLength(150);
            e.HasIndex(x => x.RequestKey).IsUnique();
            e.HasIndex(x => new { x.ManualPayingAccount, x.ManualBankReference }).IsUnique();
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.ReviewedByAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.ConfirmedByAccountId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
