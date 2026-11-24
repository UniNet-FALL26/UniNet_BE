using Microsoft.EntityFrameworkCore;
using UniNet.Domain.Entities;

namespace UniNet.Infrastructure.Data;

public static class GitEvidenceConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<GitHubProviderObject>().HasAlternateKey(x => new { x.Id, x.GitHubRepositoryId });
        model.Entity<ProjectGitRepository>().HasAlternateKey(x => new { x.Id, x.GitHubRepositoryId });
        model.Entity<GitSyncJob>().HasAlternateKey(x => new { x.Id, x.ProjectGitRepositoryId, x.GitHubRepositoryId });
        model.Entity<GitEvidenceVersion>(e =>
        {
            e.ToTable("GitEvidenceVersions", t =>
            {
                t.HasCheckConstraint("CK_GitEvidenceVersions_Versions", "\"EvidenceSchemaVersion\" > 0 AND \"CanonicalizationVersion\" > 0");
                t.HasCheckConstraint("CK_GitEvidenceVersions_Hash", "\"HashAlgorithm\" = 'SHA-256' AND length(\"ContentHash\") = 64");
            });
            e.HasKey(x => x.Id);
            e.HasAlternateKey(x => new { x.Id, x.GitHubRepositoryId });
            e.Property(x => x.HashAlgorithm).HasMaxLength(16).IsRequired();
            e.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.FrozenFactsJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.EvidenceStatus).HasMaxLength(32).IsRequired();
            e.HasOne<GitHubProviderObject>().WithMany().HasForeignKey(x => x.GitHubProviderObjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GitHubProviderObject>().WithMany().HasForeignKey(x => new { x.GitHubProviderObjectId, x.GitHubRepositoryId }).HasPrincipalKey(x => new { x.Id, x.GitHubRepositoryId }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.GitHubProviderObjectId, x.EvidenceSchemaVersion, x.CanonicalizationVersion, x.ContentHash }).IsUnique();
        });
        model.Entity<ProjectEvidenceObservation>(e =>
        {
            e.ToTable("ProjectEvidenceObservations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Category).HasMaxLength(100).IsRequired();
            e.Property(x => x.ScopeKey).HasMaxLength(512).IsRequired();
            e.Property(x => x.PageCursor).HasMaxLength(4000);
            e.Property(x => x.AuthorizationSource).HasMaxLength(32).IsRequired();
            e.Property(x => x.RetrievalCompleteness).HasMaxLength(32).IsRequired();
            e.HasOne<ProjectGitRepository>().WithMany().HasForeignKey(x => x.ProjectGitRepositoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GitSyncJob>().WithMany().HasForeignKey(x => x.GitSyncJobId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GitEvidenceVersion>().WithMany().HasForeignKey(x => x.GitEvidenceVersionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProjectGitRepository>().WithMany().HasForeignKey(x => new { x.ProjectGitRepositoryId, x.GitHubRepositoryId }).HasPrincipalKey(x => new { x.Id, x.GitHubRepositoryId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GitSyncJob>().WithMany().HasForeignKey(x => new { x.GitSyncJobId, x.ProjectGitRepositoryId, x.GitHubRepositoryId }).HasPrincipalKey(x => new { x.Id, x.ProjectGitRepositoryId, x.GitHubRepositoryId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GitEvidenceVersion>().WithMany().HasForeignKey(x => new { x.GitEvidenceVersionId, x.GitHubRepositoryId }).HasPrincipalKey(x => new { x.Id, x.GitHubRepositoryId }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ProjectGitRepositoryId, x.GitSyncJobId, x.Category, x.ScopeKey, x.GitEvidenceVersionId }).IsUnique();
            e.HasIndex(x => new { x.ProjectGitRepositoryId, x.ObservedAt });
        });
    }
}
