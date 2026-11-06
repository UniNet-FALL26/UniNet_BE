using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;

namespace UniNet.Infrastructure.Data;

public static class GitSyncConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<GitSyncJob>(e =>
        {
            e.ToTable("GitSyncJobs", t =>
            {
                t.HasCheckConstraint("CK_GitSyncJobs_Status", "\"Status\" BETWEEN 0 AND 6");
                t.HasCheckConstraint("CK_GitSyncJobs_Type", "\"SyncType\" BETWEEN 0 AND 4");
                t.HasCheckConstraint("CK_GitSyncJobs_RetryCount", "\"RetryCount\" >= 0");
                t.HasCheckConstraint("CK_GitSyncJobs_Lease", "(\"LeaseId\" IS NULL) = (\"LeaseExpiresAt\" IS NULL)");
                t.HasCheckConstraint("CK_GitSyncJobs_CompletedAt",
                    "(\"Status\" IN (2, 3, 5) AND \"CompletedAt\" IS NOT NULL) OR (\"Status\" NOT IN (2, 3, 5) AND \"CompletedAt\" IS NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.SyncType).HasConversion<short>();
            e.Property(x => x.Status).HasConversion<short>();
            e.Property(x => x.ErrorCode).HasMaxLength(100);
            e.Property(x => x.ErrorMessage).HasMaxLength(1000);
            e.Property(x => x.IdempotencyKey).HasMaxLength(255);
            e.HasIndex(x => new { x.ProjectGitRepositoryId, x.IdempotencyKey })
                .IsUnique().HasFilter("\"IdempotencyKey\" IS NOT NULL");
            e.HasIndex(x => new { x.ProjectGitRepositoryId, x.Status })
                .IsUnique().HasFilter("\"Status\" IN (0, 1, 4, 6)");
            e.HasIndex(x => new { x.Status, x.NextAttemptAt, x.LeaseExpiresAt, x.CreatedAt });
            e.HasIndex(x => new { x.ProjectGitRepositoryId, x.CreatedAt });
            e.HasOne<ProjectGitRepository>().WithMany()
                .HasForeignKey(x => x.ProjectGitRepositoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GitHubRepository>().WithMany()
                .HasForeignKey(x => x.GitHubRepositoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GitHubOAuthConnection>().WithMany()
                .HasForeignKey(x => x.OAuthConnectionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserProfile>().WithMany()
                .HasForeignKey(x => x.RequestedByProfileId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<GitSyncCheckpoint>(e =>
        {
            e.ToTable("GitSyncCheckpoints", t =>
            {
                t.HasCheckConstraint("CK_GitSyncCheckpoints_Status", "\"Status\" BETWEEN 0 AND 3");
                t.HasCheckConstraint("CK_GitSyncCheckpoints_ItemsProcessed", "\"ItemsProcessed\" >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Category).HasMaxLength(100).IsRequired();
            e.Property(x => x.ScopeKey).HasMaxLength(512).IsRequired();
            e.Property(x => x.Cursor).HasMaxLength(4000);
            e.Property(x => x.Status).HasConversion<short>();
            e.HasIndex(x => new { x.SyncJobId, x.Category, x.ScopeKey }).IsUnique();
            e.HasOne<GitSyncJob>().WithMany()
                .HasForeignKey(x => x.SyncJobId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
