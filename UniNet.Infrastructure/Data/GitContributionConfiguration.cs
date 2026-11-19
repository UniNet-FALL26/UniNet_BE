using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;

namespace UniNet.Infrastructure.Data;

public static class GitContributionConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<GitContributionRun>(entity =>
        {
            entity.ToTable("GitContributionRuns", table =>
            {
                table.HasCheckConstraint("CK_GitContributionRuns_RunNumber", "\"RunNumber\" > 0");
                table.HasCheckConstraint("CK_GitContributionRuns_Policy", "\"ScoringPolicyVersion\" IN ('ExperimentalGitScoringV1','ExperimentalGitScoringV2')");
                table.HasCheckConstraint("CK_GitContributionRuns_V2Context", "\"ScoringPolicyVersion\" <> 'ExperimentalGitScoringV2' OR length(\"CalculationContextHash\") = 64 AND \"CalculationContextHash\" IS NOT NULL");
                table.HasCheckConstraint("CK_GitContributionRuns_V2FinalReady", "\"ScoringPolicyVersion\" <> 'ExperimentalGitScoringV2' OR \"Status\" <> 3 OR (\"CalculationStatus\" = 'Ready' AND \"CalculationStatus\" IS NOT NULL AND \"NormalizedResultsJson\" IS NOT NULL AND \"NormalizationPolicyVersion\" IS NOT NULL)");
                table.HasCheckConstraint("CK_GitContributionRuns_Hash", "length(\"InputHash\") = 64");
                table.HasCheckConstraint("CK_GitContributionRuns_Status", "\"Status\" BETWEEN 0 AND 3");
                table.HasCheckConstraint("CK_GitContributionRuns_Finalization",
                    "(\"Status\" = 3 AND \"FinalizedAt\" IS NOT NULL AND \"FinalizedById\" IS NOT NULL) OR " +
                    "(\"Status\" <> 3 AND \"FinalizedAt\" IS NULL AND \"FinalizedById\" IS NULL)");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ScoringPolicyVersion).HasMaxLength(64).IsRequired();
            entity.Property(x => x.InputSnapshotJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.CategoryStatusJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.ResultsJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.ExcludedActivitiesJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.ReviewHistoryJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.InputHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ReviewFeedback).HasMaxLength(2000);
            entity.Property(x => x.Status).HasConversion<short>().IsRequired();
            entity.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProjectGitEvidenceSnapshot>().WithMany().HasForeignKey(x => new { x.GitEvidenceSnapshotId, x.ProjectId })
                .HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.CalculatedById).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.ReviewedById).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.FinalizedById).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.ProjectId, x.RunNumber }).IsUnique();
            entity.HasIndex(x => new { x.ProjectId, x.GitEvidenceSnapshotId, x.ScoringPolicyVersion }).IsUnique().HasFilter("\"ScoringPolicyVersion\" = 'ExperimentalGitScoringV1'");
            entity.HasIndex(x => new { x.ProjectId, x.CalculationContextHash }).IsUnique().HasFilter("\"CalculationContextHash\" IS NOT NULL");
            entity.HasOne<ExperimentalPeerSnapshot>().WithMany().HasForeignKey(x => new { x.PeerSnapshotId, x.ProjectId })
                .HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.CalculationContextHash).HasMaxLength(64);
            entity.Property(x => x.NormalizationPolicyVersion).HasMaxLength(64);
            entity.Property(x => x.CalculationStatus).HasMaxLength(64);
            entity.Property(x => x.NormalizedResultsJson).HasColumnType("jsonb");
        });
    }
}
