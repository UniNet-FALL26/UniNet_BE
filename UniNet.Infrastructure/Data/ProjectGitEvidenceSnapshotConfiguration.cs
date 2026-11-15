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
    }
}
