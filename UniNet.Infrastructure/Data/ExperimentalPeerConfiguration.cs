using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;

namespace UniNet.Infrastructure.Data;

public static class ExperimentalPeerConfiguration
{
    public static void Configure(ModelBuilder model, bool postgres)
    {
        model.Entity<ExperimentalPeerCohort>(e =>
        {
            e.ToTable("ExperimentalPeerCohorts", t => {
                t.HasCheckConstraint("CK_ExperimentalPeerCohorts_Status", "\"Status\" BETWEEN 0 AND 3");
                t.HasCheckConstraint("CK_ExperimentalPeerCohorts_Roster", postgres
                    ? "jsonb_typeof(\"RosterJson\") = 'array' AND jsonb_array_length(\"RosterJson\") > 0 AND length(\"RosterHash\") = 64"
                    : "json_type(\"RosterJson\") = 'array' AND json_array_length(\"RosterJson\") > 0 AND length(\"RosterHash\") = 64");
                t.HasCheckConstraint("CK_ExperimentalPeerCohorts_Closed", "(\"Status\" = 3) = (\"ClosedAt\" IS NOT NULL)");
            });
            e.HasKey(x => x.Id); e.HasAlternateKey(x => new { x.Id, x.ProjectId });
            e.Property(x => x.Status).HasConversion<short>();
            e.Property(x => x.RosterJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.RosterHash).HasMaxLength(64).IsRequired();
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.OpenedById).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ExperimentalPeerSubmission>(e =>
        {
            e.ToTable("ExperimentalPeerSubmissions", t => {
                t.HasCheckConstraint("CK_ExperimentalPeerSubmissions_Score", "\"Score\" BETWEEN 1 AND 5 AND \"Revision\" > 0 AND \"EvaluatorId\" <> \"EvaluateeId\"");
            });
            e.HasKey(x => x.Id);
            e.HasOne<ExperimentalPeerCohort>().WithMany().HasForeignKey(x => x.CohortId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.EvaluatorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.EvaluateeId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.HasIndex(x => new { x.CohortId, x.EvaluatorId, x.EvaluateeId }).IsUnique();
        });
        model.Entity<ExperimentalPeerRevision>(e =>
        {
            e.ToTable("ExperimentalPeerRevisions", t => t.HasCheckConstraint("CK_ExperimentalPeerRevisions_Score", "\"Score\" BETWEEN 1 AND 5 AND (\"PreviousScore\" IS NULL OR \"PreviousScore\" BETWEEN 1 AND 5) AND \"Revision\" > 0"));
            e.HasKey(x => x.Id);
            e.HasOne<ExperimentalPeerSubmission>().WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.SubmissionId, x.Revision }).IsUnique();
            e.Property(x => x.Comment).HasMaxLength(2000); e.Property(x => x.PreviousComment).HasMaxLength(2000);
        });
        model.Entity<ExperimentalPeerSnapshot>(e =>
        {
            e.ToTable("ExperimentalPeerSnapshots", t => t.HasCheckConstraint("CK_ExperimentalPeerSnapshots_Hash", "length(\"InputHash\") = 64"));
            e.HasKey(x => x.Id); e.HasAlternateKey(x => new { x.Id, x.ProjectId });
            e.HasIndex(x => x.CohortId).IsUnique();
            e.HasOne<ExperimentalPeerCohort>().WithMany().HasForeignKey(x => new { x.CohortId, x.ProjectId })
                .HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.InputJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.ScoresJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.InputHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.PolicyVersion).HasMaxLength(64).IsRequired();
        });
    }
}
