using Microsoft.EntityFrameworkCore;
using UniNet.Domain.Entities;

namespace UniNet.Infrastructure.Data;

public static class GitActivityConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<GitHubProviderObject>(e =>
        {
            e.ToTable("GitHubProviderObjects");
            e.HasKey(x => x.Id);
            e.Property(x => x.Category).HasMaxLength(100).IsRequired();
            e.Property(x => x.ExternalObjectId).HasMaxLength(255).IsRequired();
            e.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.RetrievalMetadataJson).HasColumnType("jsonb").IsRequired();
            e.HasIndex(x => new { x.GitHubRepositoryId, x.Category, x.ExternalObjectId }).IsUnique();
            e.HasIndex(x => new { x.GitHubRepositoryId, x.OccurredAt });
            e.HasOne<GitHubRepository>().WithMany().HasForeignKey(x => x.GitHubRepositoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<GitActivity>(e =>
        {
            e.ToTable("GitActivities");
            e.HasKey(x => x.Id);
            e.Property(x => x.ActivityType).HasMaxLength(100).IsRequired();
            e.Property(x => x.MetadataJson).HasColumnType("jsonb").IsRequired();
            e.HasIndex(x => new { x.GitHubProviderObjectId, x.ActivityType }).IsUnique();
            e.HasIndex(x => new { x.OccurredAt, x.Id });
            e.HasOne<GitHubProviderObject>().WithMany().HasForeignKey(x => x.GitHubProviderObjectId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<GitActivityActor>(e =>
        {
            e.ToTable("GitActivityActors");
            e.HasKey(x => x.Id);
            e.Property(x => x.Role).HasMaxLength(100).IsRequired();
            e.Property(x => x.ActorKey).HasMaxLength(512).IsRequired();
            e.Property(x => x.ExternalAccountId).HasMaxLength(64);
            e.Property(x => x.Login).HasMaxLength(255);
            e.HasIndex(x => new { x.GitActivityId, x.ActorKey }).IsUnique();
            e.HasOne<GitActivity>().WithMany().HasForeignKey(x => x.GitActivityId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ProjectGitActivityGrant>(e =>
        {
            e.ToTable("ProjectGitActivityGrants");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectGitRepositoryId, x.GitHubProviderObjectId }).IsUnique();
            e.HasIndex(x => new { x.ProjectGitRepositoryId, x.ObservedAt });
            e.HasOne<ProjectGitRepository>().WithMany().HasForeignKey(x => x.ProjectGitRepositoryId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GitHubProviderObject>().WithMany().HasForeignKey(x => x.GitHubProviderObjectId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<GitCommitBranchReference>(e =>
        {
            e.ToTable("GitCommitBranchReferences");
            e.HasKey(x => x.Id);
            e.Property(x => x.BranchName).HasMaxLength(255).IsRequired();
            e.HasIndex(x => new { x.GitHubProviderObjectId, x.BranchName }).IsUnique();
            e.HasOne<GitHubProviderObject>().WithMany().HasForeignKey(x => x.GitHubProviderObjectId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<GitHubWebhookDelivery>(e =>
        {
            e.ToTable("GitHubWebhookDeliveries", t =>
                t.HasCheckConstraint("CK_GitHubWebhookDeliveries_EnqueuedJobCount", "\"EnqueuedJobCount\" >= 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.DeliveryId).HasMaxLength(64).IsRequired();
            e.Property(x => x.EventName).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.DeliveryId).IsUnique();
            e.HasIndex(x => new { x.ProviderRepositoryId, x.ReceivedAt });
        });

        model.Entity<ProjectGitActivityVisibility>(e =>
        {
            e.ToTable("ProjectGitActivityVisibilities");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectGitRepositoryId, x.GitActivityId }).IsUnique();
            e.HasIndex(x => new { x.ProjectGitRepositoryId, x.ObservedAt });
            e.HasOne<ProjectGitRepository>().WithMany().HasForeignKey(x => x.ProjectGitRepositoryId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GitActivity>().WithMany().HasForeignKey(x => x.GitActivityId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
