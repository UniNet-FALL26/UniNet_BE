using Microsoft.EntityFrameworkCore;
using UniNet.Domain.Entities;
namespace UniNet.Infrastructure.Data;
public static class GitContributionAdjustmentConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<GitContributionAdjustment>(b =>
        {
            b.ToTable("GitContributionAdjustments", t => t.HasCheckConstraint("CK_GitAdjustment_State", "\"State\" IN ('Syncing','GitCaptured','PeerOpen','DraftCreated','Published','Failed')"));
            b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.Id, x.ProjectId }); b.HasIndex(x => new { x.ProjectId, x.RequestId }).IsUnique();
            b.HasIndex(x => x.ProjectId).IsUnique().HasFilter("\"State\" NOT IN ('Published','Failed')");
            b.Property(x => x.State).HasMaxLength(24); b.Property(x => x.Revision).IsConcurrencyToken();
            b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UniNet.Domain.UserProfile>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ProjectGitEvidenceSnapshot>().WithMany().HasForeignKey(x => x.GitSnapshotId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ExperimentalPeerCohort>().WithMany().HasForeignKey(x => x.PeerCohortId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OfficialContributionRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<GitContributionAdjustmentRepository>(b =>
        {
            b.ToTable("GitContributionAdjustmentRepositories"); b.HasKey(x => new { x.AdjustmentId, x.AssociationId }); b.HasIndex(x => x.JobId).IsUnique();
            b.HasOne<GitContributionAdjustment>().WithMany().HasForeignKey(x => new { x.AdjustmentId, x.ProjectId }).HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<GitSyncJob>().WithMany().HasForeignKey(x => new { x.JobId, x.AssociationId, x.RepositoryId }).HasPrincipalKey(x => new { x.Id, x.ProjectGitRepositoryId, x.GitHubRepositoryId }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
