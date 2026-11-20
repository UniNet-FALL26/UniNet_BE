using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;

namespace UniNet.Infrastructure.Data;

public static class GitHubAppInstallationConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<GitHubAppWebhookRequeueAudit>(e =>
        {
            e.ToTable("GitHubAppWebhookRequeueAudits");
            e.HasKey(x => x.Id);
            e.Property(x => x.Reason).HasMaxLength(300).IsRequired();
            e.Property(x => x.PreviousErrorCode).HasMaxLength(100).IsRequired();
            e.HasOne<GitHubAppWebhookDelivery>().WithMany().HasForeignKey(x => x.DeliveryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.ActorAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.DeliveryId, x.CreatedAt });
        });
        model.Entity<GitHubAppWorkerHeartbeat>(e =>
        {
            e.ToTable("GitHubAppWorkerHeartbeats");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UpdatedAt);
        });
        model.Entity<GitHubAppInstallation>(e =>
        {
            e.ToTable("GitHubAppInstallations", t =>
            {
                t.HasCheckConstraint("CK_GitHubAppInstallation_Status", "\"Status\" IN (0, 1, 2)");
                t.HasCheckConstraint("CK_GitHubAppInstallation_ProviderIds", "\"ProviderInstallationId\" > 0 AND \"OwnerAccountId\" > 0");
                t.HasCheckConstraint("CK_GitHubAppInstallation_ReconciliationFailures", "\"ReconciliationFailures\" >= 0 AND \"ReconciliationFailures\" <= 16");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.OwnerLogin).HasMaxLength(255).IsRequired();
            e.Property(x => x.AuthorizationGeneration).HasDefaultValue(1L);
            e.Property(x => x.OwnerType).HasMaxLength(32).IsRequired();
            e.HasIndex(x => x.ProviderInstallationId).IsUnique();
            e.HasIndex(x => x.NextReconciliationAt);
        });
        model.Entity<GitHubAppInstallationAccount>(e =>
        {
            e.ToTable("GitHubAppInstallationAccounts", t => t.HasCheckConstraint("CK_GitHubAppInstallationAccount_User", "\"GitHubUserId\" > 0"));
            e.HasKey(x => x.Id);
            e.HasOne<GitHubAppInstallation>().WithMany().HasForeignKey(x => x.InstallationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.InstallationId, x.AccountId }).IsUnique();
        });
        model.Entity<GitHubAppRepositoryGrant>(e =>
        {
            e.ToTable("GitHubAppRepositoryGrants", t => t.HasCheckConstraint("CK_GitHubAppRepositoryGrant_Repository", "\"ProviderRepositoryId\" > 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.AuthorizationGeneration).HasDefaultValue(1L);
            e.HasOne<GitHubAppInstallation>().WithMany().HasForeignKey(x => x.InstallationId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.InstallationId, x.ProviderRepositoryId }).IsUnique();
        });
        model.Entity<GitHubAppAuthorizationFlow>(e =>
        {
            e.ToTable("GitHubAppAuthorizationFlows", t => t.HasCheckConstraint("CK_GitHubAppAuthorizationFlow_Status", "\"Status\" IN (0, 1, 2, 3)"));
            e.HasKey(x => x.Id);
            e.Property(x => x.StateHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.ReturnTarget).HasMaxLength(64).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GitHubOAuthConnection>().WithMany().HasForeignKey(x => x.OAuthConnectionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.StateHash).IsUnique();
            e.HasIndex(x => new { x.AccountId, x.Status, x.ExpiresAt });
        });
        model.Entity<GitHubAppUserCredential>(e =>
        {
            e.ToTable("GitHubAppUserCredentials", t => t.HasCheckConstraint("CK_GitHubAppUserCredential_User", "\"GitHubUserId\" > 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Login).HasMaxLength(255).IsRequired();
            e.Property(x => x.AccessTokenEncrypted).IsRequired();
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AccountId, x.GitHubUserId }).IsUnique();
        });
    }
}
