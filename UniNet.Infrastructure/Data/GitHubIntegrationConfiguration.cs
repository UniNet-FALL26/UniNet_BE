using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;

namespace UniNet.Infrastructure.Data;

public static class GitHubIntegrationConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<GitHubOAuthConnection>(e =>
        {
            e.ToTable("GitHubOAuthConnections", t =>
            {
                t.HasCheckConstraint("CK_GitHubOAuthConnections_RevokedAt",
                    "(\"Status\" = 2 AND \"RevokedAt\" IS NOT NULL) OR (\"Status\" <> 2 AND \"RevokedAt\" IS NULL)");
                t.HasCheckConstraint("CK_GitHubOAuthConnections_Status", "\"Status\" BETWEEN 0 AND 2");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.ProviderAccountId).HasMaxLength(64).IsRequired();
            e.Property(x => x.Login).HasMaxLength(255).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(255);
            e.Property(x => x.AvatarUrl).HasMaxLength(2048);
            e.Property(x => x.AccessTokenEncrypted).HasColumnType("text").IsRequired();
            e.Property(x => x.RefreshTokenEncrypted).HasColumnType("text");
            e.Property(x => x.GrantedScopes).HasMaxLength(1000).IsRequired();
            e.Property(x => x.Status).HasConversion<short>();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.AccountId, x.ProviderAccountId })
                .IsUnique().HasFilter("\"Status\" = 0");
            e.HasIndex(x => x.AccountId);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<GitHubOAuthAuthorizationFlow>(e =>
        {
            e.ToTable("GitHubOAuthAuthorizationFlows", t =>
            {
                t.HasCheckConstraint("CK_GitHubOAuthAuthorizationFlows_PendingTokens",
                    "(\"Status\" <> 2) OR (\"PendingAccessTokenEncrypted\" IS NOT NULL AND \"PendingProviderAccountId\" IS NOT NULL)");
                t.HasCheckConstraint("CK_GitHubOAuthAuthorizationFlows_Status", "\"Status\" BETWEEN 0 AND 6");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.StateHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.ReturnTarget).HasMaxLength(64).IsRequired();
            e.Property(x => x.Status).HasConversion<short>();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.PendingProviderAccountId).HasMaxLength(64);
            e.Property(x => x.PendingLogin).HasMaxLength(255);
            e.Property(x => x.PendingDisplayName).HasMaxLength(255);
            e.Property(x => x.PendingAvatarUrl).HasMaxLength(2048);
            e.Property(x => x.PendingAccessTokenEncrypted).HasColumnType("text");
            e.Property(x => x.PendingRefreshTokenEncrypted).HasColumnType("text");
            e.Property(x => x.PendingGrantedScopes).HasMaxLength(1000);
            e.HasIndex(x => x.StateHash).IsUnique();
            e.HasIndex(x => new { x.AccountId, x.Status, x.ExpiresAt });
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<GitHubRepository>(e =>
        {
            e.ToTable("GitHubRepositories", t => t.HasCheckConstraint(
                "CK_GitHubRepositories_ProviderIds", "\"ProviderRepositoryId\" > 0 AND \"ProviderOwnerId\" > 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.OwnerLogin).HasMaxLength(255).IsRequired();
            e.Property(x => x.Name).HasMaxLength(255).IsRequired();
            e.Property(x => x.FullName).HasMaxLength(511).IsRequired();
            e.Property(x => x.HtmlUrl).HasMaxLength(2048).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.DefaultBranch).HasMaxLength(255);
            e.HasIndex(x => x.ProviderRepositoryId).IsUnique();
            e.HasIndex(x => x.FullName);
        });

        model.Entity<ProjectGitManagerGrant>(e =>
        {
            e.ToTable("ProjectGitManagerGrants", t =>
            {
                t.HasCheckConstraint("CK_ProjectGitManagerGrants_ActiveRevocation",
                    "(\"IsActive\" AND \"RevokedAt\" IS NULL AND \"RevokedByProfileId\" IS NULL) OR (NOT \"IsActive\" AND \"RevokedAt\" IS NOT NULL)");
                t.HasCheckConstraint("CK_ProjectGitManagerGrants_DifferentProfiles",
                    "\"UserProfileId\" <> \"GrantedByProfileId\"");
            });
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.UserProfileId })
                .IsUnique().HasFilter("\"IsActive\"");
            e.HasIndex(x => new { x.ProjectId, x.UserProfileId, x.IsActive });
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.UserProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.GrantedByProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.RevokedByProfileId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ProjectGitRepository>(e =>
        {
            e.ToTable("ProjectGitRepositories", t =>
            {
                t.HasCheckConstraint("CK_ProjectGitRepositories_UnlinkedAt",
                    "(\"UnlinkedAt\" IS NULL) OR (\"UnlinkedAt\" >= \"LinkedAt\")");
                t.HasCheckConstraint("CK_ProjectGitRepositories_AccessStatus", "\"AccessStatus\" BETWEEN 0 AND 2");
                t.HasCheckConstraint("CK_ProjectGitRepositories_SyncIntervalHours", "\"SyncIntervalHours\" > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.AccessStatus).HasConversion<short>();
            e.Property(x => x.SyncIntervalHours).HasDefaultValue(6);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.ProjectId, x.GitHubRepositoryId })
                .IsUnique().HasFilter("\"UnlinkedAt\" IS NULL");
            e.HasIndex(x => new { x.GitHubRepositoryId, x.AccessStatus, x.UnlinkedAt });
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GitHubRepository>().WithMany().HasForeignKey(x => x.GitHubRepositoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GitHubOAuthConnection>().WithMany().HasForeignKey(x => x.OAuthConnectionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.LinkedByProfileId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ProjectGitRepositoryHistory>(e =>
        {
            e.ToTable("ProjectGitRepositoryHistory", t =>
                t.HasCheckConstraint("CK_ProjectGitRepositoryHistory_Action", "\"Action\" BETWEEN 0 AND 6"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasConversion<short>();
            e.Property(x => x.Details).HasMaxLength(2000);
            e.HasIndex(x => new { x.ProjectGitRepositoryId, x.CreatedAt });
            e.HasOne<ProjectGitRepository>().WithMany().HasForeignKey(x => x.ProjectGitRepositoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.ActorProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GitHubOAuthConnection>().WithMany().HasForeignKey(x => x.PreviousOAuthConnectionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GitHubOAuthConnection>().WithMany().HasForeignKey(x => x.NewOAuthConnectionId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
