using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UniNet.Domain.Entities;
using UniNet.Domain;

namespace UniNet.Infrastructure.Data;

public sealed class OfficialWorkflowConfiguration : IEntityTypeConfiguration<ProjectContributionWorkflow>
{
    public void Configure(EntityTypeBuilder<ProjectContributionWorkflow> b)
    {
        b.ToTable("ProjectContributionWorkflows", t => t.HasCheckConstraint("CK_OfficialWorkflow_Mode", "\"Mode\" IN ('OfficialV2','LegacyV1','RequiresOptIn')"));
        b.HasKey(x => x.ProjectId); b.Property(x => x.Mode).HasMaxLength(32).IsRequired();
        b.HasOne<Project>().WithOne().HasForeignKey<ProjectContributionWorkflow>(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}
public sealed class OfficialRunConfiguration : IEntityTypeConfiguration<OfficialContributionRun>
{
    public void Configure(EntityTypeBuilder<OfficialContributionRun> b)
    {
        b.ToTable("OfficialContributionRuns", t => {
            t.HasCheckConstraint("CK_OfficialRun_State", "\"State\" IN ('Draft','Review','Approved','Rejected','Finalized')");
            t.HasCheckConstraint("CK_OfficialRun_Finalized", "(\"State\" = 'Finalized') = (\"FinalizedAt\" IS NOT NULL AND \"FinalizedById\" IS NOT NULL)");
            t.HasCheckConstraint("CK_OfficialRun_Revision", "\"Revision\" >= 0 AND length(\"ResultHash\") = 64");
        });
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.Id, x.ProjectId });
        b.Property(x => x.State).HasMaxLength(32).IsRequired(); b.Property(x => x.ResultHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasIndex(x => new { x.ProjectId, x.RequestId }).IsUnique();
        b.HasIndex(x => x.ProjectId).IsUnique().HasFilter("\"State\" = 'Finalized'");
        b.HasOne<GitContributionRun>().WithOne().HasForeignKey<OfficialContributionRun>(x => new { x.Id, x.ProjectId })
            .HasPrincipalKey<GitContributionRun>(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProjectContributionWorkflow>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.FinalizedById).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class OfficialAuditConfiguration : IEntityTypeConfiguration<OfficialContributionAudit>
{
    public void Configure(EntityTypeBuilder<OfficialContributionAudit> b)
    {
        b.ToTable("OfficialContributionAudits"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.RunId, x.Revision }).IsUnique();
        b.Property(x => x.Action).HasMaxLength(32).IsRequired(); b.Property(x => x.Feedback).HasMaxLength(2000);
        b.HasOne<OfficialContributionRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class OfficialPublicationConfiguration : IEntityTypeConfiguration<OfficialContributionPublication>
{
    public void Configure(EntityTypeBuilder<OfficialContributionPublication> b)
    {
        b.ToTable("OfficialContributionPublications"); b.HasKey(x => x.ProjectId); b.HasIndex(x => x.RunId).IsUnique();
        b.HasOne<OfficialContributionRun>().WithOne().HasForeignKey<OfficialContributionPublication>(x => new { x.RunId, x.ProjectId })
            .HasPrincipalKey<OfficialContributionRun>(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.PublishedById).OnDelete(DeleteBehavior.Restrict);
    }
}
