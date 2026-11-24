using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;

namespace UniNet.Infrastructure.Data;

public static class ProjectGitMemberIdentityConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<ProjectGitMemberIdentity>(entity =>
        {
            entity.ToTable("ProjectGitMemberIdentities", table =>
            {
                table.HasCheckConstraint("CK_ProjectGitMemberIdentities_ExternalAccountId",
                    "\"ExternalAccountId\" > 0");
                table.HasCheckConstraint("CK_ProjectGitMemberIdentities_Status",
                    "\"Status\" BETWEEN 0 AND 1");
                table.HasCheckConstraint("CK_ProjectGitMemberIdentities_UnlinkedAt",
                    "(\"Status\" = 0 AND \"UnlinkedAt\" IS NULL) OR (\"Status\" = 1 AND \"UnlinkedAt\" IS NOT NULL)");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ExternalUsername).HasMaxLength(255).IsRequired();
            entity.Property(x => x.Status).HasConversion<short>();
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => new { x.ProjectId, x.Provider, x.ExternalAccountId })
                .IsUnique().HasFilter("\"Status\" = 0");
            entity.HasIndex(x => new { x.ProjectId, x.UserProfileId, x.Status });
            entity.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.UserProfileId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GitHubOAuthConnection>().WithMany()
                .HasForeignKey(x => x.VerifiedByConnectionId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ProjectGitMemberIdentityHistory>(entity =>
        {
            entity.ToTable("ProjectGitMemberIdentityHistory", table =>
            {
                table.HasCheckConstraint("CK_ProjectGitMemberIdentityHistory_ExternalAccountId",
                    "\"ExternalAccountId\" > 0");
                table.HasCheckConstraint("CK_ProjectGitMemberIdentityHistory_Action",
                    "\"Action\" BETWEEN 0 AND 5");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ExternalUsername).HasMaxLength(255).IsRequired();
            entity.Property(x => x.PreviousExternalUsername).HasMaxLength(255);
            entity.Property(x => x.Action).HasConversion<short>();
            entity.Property(x => x.PreviousStatus).HasConversion<short?>();
            entity.Property(x => x.NewStatus).HasConversion<short?>();
            entity.Property(x => x.OccurredAt)
                .HasConversion(value => value.UtcDateTime,
                    value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
            entity.HasIndex(x => new { x.IdentityId, x.OccurredAt });
            entity.HasIndex(x => new { x.ProjectId, x.OccurredAt });
            entity.HasOne<ProjectGitMemberIdentity>().WithMany()
                .HasForeignKey(x => x.IdentityId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.UserProfileId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.PerformedByProfileId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GitHubOAuthConnection>().WithMany()
                .HasForeignKey(x => x.VerifiedByConnectionId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
