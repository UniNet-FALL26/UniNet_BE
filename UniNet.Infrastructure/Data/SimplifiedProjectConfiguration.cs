using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;

namespace UniNet.Infrastructure.Data;

public static class SimplifiedProjectConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<Project>().Navigation(x => x.Recruitment).AutoInclude();
        model.Entity<Project>().Property(x => x.LogoUrl).HasColumnType("text");
        model.Entity<Project>().Property(x => x.Technologies).HasColumnType("jsonb");

        model.Entity<ProjectDetail>(e =>
        {
            e.ToTable("ProjectDetails");
            e.HasKey(x => x.ProjectId);
            e.Property(x => x.Description).HasColumnType("text");
            foreach (var name in new[] { "Features", "Results", "Documents", "Media", "Links" })
                e.Property<string?>(name).HasColumnType("jsonb");
            e.Property(x => x.StartDate).HasColumnType("date");
            e.Property(x => x.ActualEndDate).HasColumnType("date");
            e.HasOne(x => x.Project).WithOne(x => x.Details)
                .HasForeignKey<ProjectDetail>(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<ProjectRecruitment>(e =>
        {
            e.ToTable("ProjectRecruitments", t =>
                t.HasCheckConstraint("CK_ProjectRecruitments_Status", "\"Status\" BETWEEN 0 AND 2"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Description).HasColumnType("text");
            e.Property(x => x.Positions).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Status).HasConversion<short>();
            e.HasIndex(x => x.ProjectId).IsUnique();
            e.HasOne(x => x.Project).WithOne(x => x.Recruitment)
                .HasForeignKey<ProjectRecruitment>(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<ProjectPersonalReview>(e =>
        {
            e.ToTable("ProjectPersonalReviews");
            e.HasKey(x => x.Id);
            e.Property(x => x.Challenges).HasColumnType("text");
            e.Property(x => x.LessonsLearned).HasColumnType("text");
            e.HasIndex(x => new { x.ProjectId, x.UserId }).IsUnique();
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ProjectJoinRequest>(e =>
        {
            e.Property(x => x.RequestType).HasConversion<short>();
            e.Property(x => x.PositionId).HasMaxLength(100);
            e.HasOne(x => x.Inviter).WithMany().HasForeignKey(x => x.InviterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.ReviewedBy).OnDelete(DeleteBehavior.Restrict);
            e.ToTable("ProjectJoinRequests", t =>
                t.HasCheckConstraint("CK_ProjectJoinRequests_RequestType", "\"RequestType\" BETWEEN 0 AND 1"));
        });
        model.Entity<ProjectMember>(e =>
        {
            e.Property(x => x.PositionId).HasMaxLength(100);
            e.HasOne<ProjectJoinRequest>().WithMany().HasForeignKey(x => x.JoinRequestId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
