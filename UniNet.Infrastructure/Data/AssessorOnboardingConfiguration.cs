using Microsoft.EntityFrameworkCore;
using UniNet.Domain;

namespace UniNet.Infrastructure.Data;

internal static class AssessorOnboardingConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<AssessorOnboarding>(entity =>
        {
            entity.Property(x => x.EmailNormalized).HasMaxLength(255);
            entity.Property(x => x.FullName).HasMaxLength(255);
            entity.Property(x => x.Status).HasConversion<short>().IsConcurrencyToken();
            entity.Property(x => x.Revision).IsConcurrencyToken();
            entity.Property(x => x.ProfileTokenHash).HasMaxLength(64);
            entity.HasIndex(x => x.ProfileTokenHash).IsUnique();
            entity.HasIndex(x => x.ContractTokenHash).IsUnique();
            entity.HasIndex(x => x.ReadSessionHash).IsUnique();
            entity.HasIndex(x => x.EmailNormalized).IsUnique()
                .HasDatabaseName("UX_AssessorOnboardings_OpenEmail")
                .HasFilter("\"Status\" IN (0, 1, 2, 3, 4, 9)");
            entity.HasIndex(x => x.AccountId).IsUnique();
            entity.HasIndex(x => new { x.Status, x.AutoApproveAt });
            entity.HasIndex(x => new { x.Status, x.GenerationRetryAt });
            entity.HasOne<Account>().WithMany().HasForeignKey(x => x.CreatedByAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Account>().WithMany().HasForeignKey(x => x.ReviewedByAccountId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<AssessorOnboardingEmail>(entity =>
        {
            entity.HasIndex(x => x.DeduplicationKey).IsUnique();
            entity.HasIndex(x => x.NextAttemptAt).HasFilter("\"SentAt\" IS NULL");
            entity.HasIndex(x => new { x.OnboardingId, x.Kind, x.CreatedAt });
            entity.HasOne<AssessorOnboarding>().WithMany().HasForeignKey(x => x.OnboardingId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
