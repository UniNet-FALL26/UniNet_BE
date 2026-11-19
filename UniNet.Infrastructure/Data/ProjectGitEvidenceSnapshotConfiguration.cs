using Microsoft.EntityFrameworkCore;
using UniNet.Domain.Entities;

namespace UniNet.Infrastructure.Data;

public static class ProjectGitEvidenceSnapshotConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<ProjectEvidenceObservation>().HasAlternateKey(x => new { x.Id, x.GitEvidenceVersionId, x.ProjectGitRepositoryId });
        model.Entity<ProjectGitRepository>().HasAlternateKey(x => new { x.Id, x.ProjectId });
        model.Entity<ProjectGitEvidenceSnapshot>(e =>
        {
            e.ToTable("ProjectGitEvidenceSnapshots");
            e.HasKey(x => x.Id);
            e.HasAlternateKey(x => new { x.Id, x.ProjectId });
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ProjectId, x.CreatedAt });
        });
        model.Entity<ProjectGitEvidenceSnapshotEntry>(e =>
        {
            e.ToTable("ProjectGitEvidenceSnapshotEntries");
            e.HasKey(x => new { x.SnapshotId, x.ProjectEvidenceObservationId });
            e.HasOne<ProjectGitEvidenceSnapshot>().WithMany().HasForeignKey(x => new { x.SnapshotId, x.ProjectId })
                .HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProjectEvidenceObservation>().WithMany()
                .HasForeignKey(x => new { x.ProjectEvidenceObservationId, x.GitEvidenceVersionId, x.ProjectGitRepositoryId })
                .HasPrincipalKey(x => new { x.Id, x.GitEvidenceVersionId, x.ProjectGitRepositoryId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProjectGitRepository>().WithMany().HasForeignKey(x => new { x.ProjectGitRepositoryId, x.ProjectId })
                .HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<GitSyncCheckpoint>().HasAlternateKey(x => new { x.Id, x.SyncJobId });
        model.Entity<ProjectGitEvidenceSnapshotCoverage>(e =>
        {
            e.ToTable("ProjectGitEvidenceSnapshotCoverage", t => t.HasCheckConstraint("CK_SnapshotCoverage_Receipt",
                "\"ItemsProcessed\" >= 0 AND \"ReceiptPolicyVersion\" = 1 AND \"ScopeCompletedAt\" <= \"JobCompletedAt\""));
            e.HasKey(x => new { x.SnapshotId, x.GitSyncCheckpointId });
            e.Property(x => x.Category).HasMaxLength(100).IsRequired();
            e.Property(x => x.ScopeKey).HasMaxLength(512).IsRequired();
            e.HasIndex(x => new { x.SnapshotId, x.GitSyncJobId, x.Category, x.ScopeKey }).IsUnique();
            e.HasOne<ProjectGitEvidenceSnapshot>().WithMany().HasForeignKey(x => new { x.SnapshotId, x.ProjectId })
                .HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProjectGitRepository>().WithMany().HasForeignKey(x => new { x.ProjectGitRepositoryId, x.ProjectId })
                .HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GitSyncJob>().WithMany().HasForeignKey(x => new { x.GitSyncJobId, x.ProjectGitRepositoryId })
                .HasPrincipalKey(x => new { x.Id, x.ProjectGitRepositoryId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GitSyncCheckpoint>().WithMany().HasForeignKey(x => new { x.GitSyncCheckpointId, x.GitSyncJobId })
                .HasPrincipalKey(x => new { x.Id, x.SyncJobId }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
